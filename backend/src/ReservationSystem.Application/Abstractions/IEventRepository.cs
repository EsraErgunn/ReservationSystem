using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct);
}
