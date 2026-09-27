using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;

namespace ReservationSystem.Infrastructure.Persistence.Queries;

/// <summary>
/// FR-03 / NFR-01. Okuma tarafı domain'den geçmez: AsNoTracking + projeksiyon,
/// domain entity'leri hiç materyalize edilmez.
/// </summary>
public class GetSeatMapQueryHandler(AppDbContext context)
    : IQueryHandler<GetSeatMapQuery, SeatMapDto>
{
    public async Task<SeatMapDto> HandleAsync(GetSeatMapQuery query, CancellationToken ct)
    {
        var @event = await context.Events
            .AsNoTracking()
            .Where(e => e.Id == query.EventId)
            .Select(e => new { e.Id, e.Title })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundAppException("event", query.EventId);

        var seats = await context.EventSeats
            .AsNoTracking()
            .Where(es => es.EventId == query.EventId)
            .Join(context.Seats, es => es.SeatId, s => s.Id, (es, s) => new { es, s })
            // Sıralama projeksiyondan ÖNCE: EF Core, record constructor'ına yapılmış
            // projeksiyonun üyeleri üzerinden ORDER BY üretemez.
            .OrderBy(x => x.s.RowLabel)
            .ThenBy(x => x.s.SeatNumber)
            .Select(x => new SeatMapItemDto(
                x.es.Id,
                x.s.RowLabel,
                x.s.SeatNumber,
                x.es.Price,
                x.es.Status.ToString()))
            .ToListAsync(ct);

        return new SeatMapDto(@event.Id, @event.Title, seats);
    }
}
