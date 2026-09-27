using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>FR-12: EventSeat'ler aggregate koleksiyonu üzerinden birlikte eklenir.</summary>
    void Add(Event @event);
}
