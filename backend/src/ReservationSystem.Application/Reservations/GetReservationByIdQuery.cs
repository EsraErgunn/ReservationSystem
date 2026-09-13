using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Application.Reservations;

/// <summary>FR-10: tek rezervasyonun detayı.</summary>
public record GetReservationByIdQuery(Guid Id) : IQuery<ReservationDetailDto>;

/// <summary>
/// Yetki kontrolü controller'da değil burada: controller ince kalmalı ve aynı kural
/// başka bir giriş noktasından çağrılsa da geçerli olmalı.
/// <para>
/// 404 → 403 sıralaması bilinçli (feature-auth-plani.md §5): başkasının kaydı için de
/// 404 dönmek varlığı hiç sızdırmazdı ama hata ayıklamayı zorlaştırır. Kabul edilen
/// risk, GUID v7 kimlikleri tahmin edilemez olduğu için pratikte önemsiz.
/// </para>
/// </summary>
public class GetReservationByIdHandler(
    IReservationQueries queries,
    ICurrentUser currentUser)
    : IQueryHandler<GetReservationByIdQuery, ReservationDetailDto>
{
    public async Task<ReservationDetailDto> HandleAsync(
        GetReservationByIdQuery query, CancellationToken ct)
    {
        // BR-14
        var userId = currentUser.UserId ?? throw new UnauthorizedAppException();

        var result = await queries.GetDetailAsync(query.Id, ct)
            ?? throw new NotFoundAppException("reservation", query.Id);

        // BR-15
        if (result.UserId != userId && !currentUser.IsAdmin)
            throw new ForbiddenAppException();

        return result.Detail;
    }
}
