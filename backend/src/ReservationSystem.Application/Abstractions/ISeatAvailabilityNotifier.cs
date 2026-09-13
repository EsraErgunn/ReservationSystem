namespace ReservationSystem.Application.Abstractions;

public interface ISeatAvailabilityNotifier
{
    /// <summary>FR-09: koltuk durumu değişince diğer kullanıcılara yayın.</summary>
    Task NotifySeatsChangedAsync(
        Guid eventId, IReadOnlyList<Guid> seatIds, CancellationToken ct);
}
