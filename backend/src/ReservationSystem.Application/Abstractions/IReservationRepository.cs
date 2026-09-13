using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Ödeme akışı için: Items ve Payments dahil yükler.</summary>
    Task<Reservation?> GetWithPaymentsAsync(Guid id, CancellationToken ct);

    Task<Reservation?> GetByPaymentTokenAsync(string token, CancellationToken ct);

    /// <summary>BR-02: süresi dolmuş hold'lar.</summary>
    Task<IReadOnlyList<Reservation>> GetExpiredAsync(
        DateTime utcNow, int batchSize, CancellationToken ct);

    void Add(Reservation reservation);
}
