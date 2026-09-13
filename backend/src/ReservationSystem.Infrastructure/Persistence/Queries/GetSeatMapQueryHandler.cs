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
            .Join(context.Seats, es => es.SeatId, s => s.Id, (es, s) => new SeatMapItemDto(
                es.Id,
                s.RowLabel,
                s.SeatNumber,
                es.Price,
                es.Status.ToString()))
            .OrderBy(s => s.RowLabel)
            .ThenBy(s => s.SeatNumber)
            .ToListAsync(ct);

        return new SeatMapDto(@event.Id, @event.Title, seats);
    }
}
