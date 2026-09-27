using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// FR-10 okuma tarafı. Komut tarafındaki <see cref="IReservationRepository"/>'den
/// bilinçli olarak ayrı: bu port domain entity döndürmez, doğrudan okuma modeline
/// projeksiyon yapar (CQRS okuma tarafı, <c>GetSeatMapQueryHandler</c> ile aynı desen).
/// </summary>
public interface IReservationQueries
{
    Task<IReadOnlyList<ReservationSummaryDto>> GetByUserAsync(
        Guid userId, CancellationToken ct);

    /// <summary>Yetki kontrolü için UserId taşıyan iç tip döner.</summary>
    Task<ReservationDetailWithOwnerDto?> GetDetailAsync(
        Guid id, CancellationToken ct);
}
