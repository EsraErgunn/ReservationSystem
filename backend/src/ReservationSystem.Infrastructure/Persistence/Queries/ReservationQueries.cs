using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Reservations.Dtos;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Infrastructure.Persistence.Queries;

/// <summary>
/// FR-10 okuma tarafı: <c>AsNoTracking</c> + doğrudan projeksiyon, domain entity
/// hiç materyalize edilmiyor.
/// <para>
/// <c>Reservation</c> üzerinde <c>Event</c> navigasyon özelliği yok — ilişki
/// <c>ReservationConfiguration</c>'da <c>HasOne&lt;Event&gt;().WithMany()</c> ile
/// gölge olarak kuruluyor ki domain modeli temiz kalsın. Bu yüzden açık
/// <c>Join</c>, <c>GetSeatMapQueryHandler</c> ile aynı desende.
/// </para>
/// </summary>
public class ReservationQueries(AppDbContext context) : IReservationQueries
{
    /// <summary>ix_reservations_user (user_id, created_at DESC) bu sorgu için var.</summary>
    public async Task<IReadOnlyList<ReservationSummaryDto>> GetByUserAsync(
        Guid userId, CancellationToken ct) =>
        await context.Reservations
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Join(context.Events, r => r.EventId, e => e.Id, (r, e) => new ReservationSummaryDto(
                r.Id,
                r.EventId,
                e.Title,
                e.EventDate,
                r.Status.ToString(),
                // Yalnızca aktif satırlar: iptal edilen koltuk sayıya girmemeli.
                r.Items.Count(i => i.IsActive),
                r.TotalAmount,
                r.Status == ReservationStatus.Held ? (DateTime?)r.HeldUntil : null,
                r.CreatedAt))
            .ToListAsync(ct);

    public async Task<ReservationDetailWithOwnerDto?> GetDetailAsync(
        Guid id, CancellationToken ct)
    {
        var header = await context.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Join(context.Events, r => r.EventId, e => e.Id, (r, e) => new { r, e })
            .Join(context.Venues, x => x.e.VenueId, v => v.Id, (x, v) => new
            {
                x.r.Id,
                x.r.UserId,
                x.r.EventId,
                EventTitle = x.e.Title,
                x.e.EventDate,
                VenueName = v.Name,
                Status = x.r.Status.ToString(),
                x.r.TotalAmount,
                HeldUntil = x.r.Status == ReservationStatus.Held ? (DateTime?)x.r.HeldUntil : null,
                x.r.CreatedAt,

                // Reservation üzerinde payment_status kolonu bilinçli olarak yok
                // (veritabani-semasi.md §2.8); güncel durum burada türetiliyor.
                LastPaymentStatus = x.r.Payments
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => p.Status.ToString())
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (header is null) return null;

        var seats = await context.ReservationItems
            .AsNoTracking()
            .Where(i => i.ReservationId == id && i.IsActive)
            .Join(context.EventSeats, i => i.EventSeatId, es => es.Id, (i, es) => new { i, es })
            .Join(context.Seats, x => x.es.SeatId, s => s.Id, (x, s) => new ReservationSeatDto(
                x.es.Id,
                s.RowLabel,
                s.SeatNumber,
                x.i.PriceAtReservation))
            .OrderBy(s => s.RowLabel)
            .ThenBy(s => s.SeatNumber)
            .ToListAsync(ct);

        return new ReservationDetailWithOwnerDto(
            header.UserId,
            new ReservationDetailDto(
                header.Id,
                header.EventId,
                header.EventTitle,
                header.EventDate,
                header.VenueName,
                header.Status,
                header.TotalAmount,
                header.HeldUntil,
                header.CreatedAt,
                seats,
                header.LastPaymentStatus));
    }
}
