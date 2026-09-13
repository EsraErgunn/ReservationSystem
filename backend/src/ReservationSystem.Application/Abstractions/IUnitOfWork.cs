namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// Transaction sınırı. Implementasyon, EF Core / Npgsql istisnalarını
/// Application'ın tanıdığı tiplere çevirmekle yükümlüdür
/// (<c>ConcurrencyConflictException</c>, <c>UniqueConstraintException</c>).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// F maddesi: Reservation + ReservationItem + EventSeat aynı transaction'da
    /// güncellenmeli. Ödeme akışında birden fazla SaveChanges gerektiği için
    /// açık transaction şart.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
