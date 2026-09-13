using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class EventRepository(AppDbContext context) : IEventRepository
{
    public Task<Event?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
}
