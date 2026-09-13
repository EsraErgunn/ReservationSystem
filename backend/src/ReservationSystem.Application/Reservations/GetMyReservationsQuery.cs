using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Application.Reservations;

/// <summary>FR-10: kullanıcının kendi rezervasyonları, en yeniden eskiye.</summary>
public record GetMyReservationsQuery : IQuery<IReadOnlyList<ReservationSummaryDto>>;

public class GetMyReservationsHandler(
    IReservationQueries queries,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyReservationsQuery, IReadOnlyList<ReservationSummaryDto>>
{
    public async Task<IReadOnlyList<ReservationSummaryDto>> HandleAsync(
        GetMyReservationsQuery query, CancellationToken ct)
    {
        // BR-14
        var userId = currentUser.UserId ?? throw new UnauthorizedAppException();

        return await queries.GetByUserAsync(userId, ct);
    }
}
