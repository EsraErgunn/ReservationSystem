using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class EventSeatRepository(AppDbContext context) : IEventSeatRepository
{
    /// <summary>Takip edilir (tracked) yükleme — xmin'i EF Core yönetir.</summary>
    public async Task<IReadOnlyList<EventSeat>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct) =>
        await context.EventSeats
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EventSeat>> GetByReservationAsync(
        Guid reservationId, CancellationToken ct) =>
        await context.EventSeats
            .Where(s => context.ReservationItems
                .Any(i => i.ReservationId == reservationId && i.EventSeatId == s.Id))
            .ToListAsync(ct);
}
