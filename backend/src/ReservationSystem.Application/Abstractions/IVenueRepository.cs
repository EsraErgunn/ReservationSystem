using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

/// <summary>FR-12: mekân ve fiziksel koltuk düzeni (admin).</summary>
public interface IVenueRepository
{
    Task<Venue?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Etkinlik koltukları üretilirken mekânın tüm koltukları gerekir.</summary>
    Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid venueId, CancellationToken ct);

    void Add(Venue venue, IReadOnlyList<Seat> seats);
}
