using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

public interface IEventSeatRepository
{
    /// <summary>
    /// Belirtilen koltukları takip edilir (tracked) şekilde yükler.
    /// Concurrency token'ı EF Core yönetir; çağıran taraf bilmez.
    /// </summary>
    Task<IReadOnlyList<EventSeat>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<EventSeat>> GetByReservationAsync(
        Guid reservationId, CancellationToken ct);
}
