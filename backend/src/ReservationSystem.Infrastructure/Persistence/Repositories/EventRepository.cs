using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class EventRepository(AppDbContext context) : IEventRepository
{
    public Task<Event?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Events.FirstOrDefaultAsync(e => e.Id == id, ct);

    // EventSeats backing field üzerinden aynı SaveChanges'te eklenir.
    public void Add(Event @event) => context.Events.Add(@event);
}
