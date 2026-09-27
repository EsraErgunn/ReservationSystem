using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;
using ReservationSystem.Domain.Enums;
using ReservationSystem.Infrastructure.Caching;

namespace ReservationSystem.Infrastructure.Persistence.Queries;

/// <summary>
/// FR-02 / NFR-02. Liste sonucu Redis'te kısa süreli tutulur. Müsait koltuk sayısı
/// en fazla <see cref="EventCatalogCache.Ttl"/> kadar bayat olabilir — kabul edilen
/// takas: listedeki sayı bilgilendirme amaçlı, asıl müsaitlik koltuk haritasından
/// (ve SignalR'dan) gelir. Cache erişilemezse sorgu doğrudan veritabanına düşer.
/// </summary>
public class GetEventsQueryHandler(
    AppDbContext context,
    IDistributedCache cache,
    TimeProvider clock,
    ILogger<GetEventsQueryHandler> logger)
    : IQueryHandler<GetEventsQuery, IReadOnlyList<EventSummaryDto>>
{
    public async Task<IReadOnlyList<EventSummaryDto>> HandleAsync(
        GetEventsQuery query, CancellationToken ct)
    {
        var key = EventCatalogCache.ListKey(query.IncludePast);

        var cached = await TryGetAsync(key, ct);
        if (cached is not null) return cached;

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var events = await context.Events
            .AsNoTracking()
            .Where(e => query.IncludePast || e.EventDate >= utcNow)
            .OrderBy(e => e.EventDate)
            .Join(context.Venues, e => e.VenueId, v => v.Id, (e, v) => new EventSummaryDto(
                e.Id,
                e.Title,
                e.Description,
                e.EventDate,
                e.SalesStartAt,
                e.SalesEndAt,
                v.Id,
                v.Name,
                v.City,
                e.EventSeats.Min(s => (decimal?)s.Price) ?? 0m,
                e.EventSeats.Max(s => (decimal?)s.Price) ?? 0m,
                e.EventSeats.Count(s => s.Status == SeatStatus.Available),
                e.EventSeats.Count()))
            .ToListAsync(ct);

        await TrySetAsync(key, events, ct);
        return events;
    }

    private async Task<List<EventSummaryDto>?> TryGetAsync(string key, CancellationToken ct)
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? null : JsonSerializer.Deserialize<List<EventSummaryDto>>(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Etkinlik cache'i okunamadı, veritabanına düşülüyor.");
            return null;
        }
    }

    private async Task TrySetAsync(string key, List<EventSummaryDto> value, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = EventCatalogCache.Ttl },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Etkinlik cache'i yazılamadı.");
        }
    }
}

/// <summary>FR-02: detay sayfası. Cache'lenmiyor — tek satır, index üzerinden.</summary>
public class GetEventByIdQueryHandler(AppDbContext context)
    : IQueryHandler<GetEventByIdQuery, EventDetailDto>
{
    public async Task<EventDetailDto> HandleAsync(GetEventByIdQuery query, CancellationToken ct) =>
        await context.Events
            .AsNoTracking()
            .Where(e => e.Id == query.Id)
            .Join(context.Venues, e => e.VenueId, v => v.Id, (e, v) => new EventDetailDto(
                e.Id,
                e.Title,
                e.Description,
                e.EventDate,
                e.SalesStartAt,
                e.SalesEndAt,
                v.Id,
                v.Name,
                v.Address,
                v.City,
                e.EventSeats.Min(s => (decimal?)s.Price) ?? 0m,
                e.EventSeats.Max(s => (decimal?)s.Price) ?? 0m,
                e.EventSeats.Count(s => s.Status == SeatStatus.Available),
                e.EventSeats.Count()))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundAppException("event", query.Id);
}

/// <summary>FR-13: satış durumu. Yetki kontrolü controller'daki Admin politikasında.</summary>
public class GetEventSalesQueryHandler(AppDbContext context)
    : IQueryHandler<GetEventSalesQuery, EventSalesDto>
{
    public async Task<EventSalesDto> HandleAsync(GetEventSalesQuery query, CancellationToken ct)
    {
        var header = await context.Events
            .AsNoTracking()
            .Where(e => e.Id == query.EventId)
            .Select(e => new { e.Id, e.Title, e.EventDate })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundAppException("event", query.EventId);

        var seatCounts = await context.EventSeats
            .AsNoTracking()
            .Where(s => s.EventId == query.EventId)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var reservationCounts = await context.Reservations
            .AsNoTracking()
            .Where(r => r.EventId == query.EventId)
            .GroupBy(r => r.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Amount = g.Sum(r => r.TotalAmount)
            })
            .ToListAsync(ct);

        int Seats(SeatStatus status) => seatCounts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return new EventSalesDto(
            header.Id,
            header.Title,
            header.EventDate,
            TotalSeats: seatCounts.Sum(c => c.Count),
            AvailableSeats: Seats(SeatStatus.Available),
            HeldSeats: Seats(SeatStatus.Held),
            SoldSeats: Seats(SeatStatus.Sold),
            ConfirmedRevenue: reservationCounts
                .Where(r => r.Status == ReservationStatus.Confirmed)
                .Sum(r => r.Amount),
            ReservationsByStatus: Enum.GetValues<ReservationStatus>()
                .ToDictionary(
                    s => s.ToString(),
                    s => reservationCounts.FirstOrDefault(r => r.Status == s)?.Count ?? 0));
    }
}
