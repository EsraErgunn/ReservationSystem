using Microsoft.Extensions.Options;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments.Models;

namespace ReservationSystem.Application.Payments;

public record StartPaymentCommand(Guid ReservationId) : ICommand<StartPaymentResult>;

public record StartPaymentResult(string FormContent);

public class StartPaymentHandler(
    IReservationRepository reservations,
    IUserRepository users,
    IPaymentGateway gateway,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<PaymentOptions> options)
    : ICommandHandler<StartPaymentCommand, StartPaymentResult>
{
    public async Task<StartPaymentResult> HandleAsync(
        StartPaymentCommand command, CancellationToken ct)
    {
        // BR-14
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAppException();

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetWithPaymentsAsync(command.ReservationId, ct)
            ?? throw new NotFoundAppException("reservation", command.ReservationId);

        // BR-13: sadece kendi rezervasyonun
        if (reservation.UserId != userId)
            throw new ForbiddenAppException();

        var buyer = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundAppException("user", userId);

        // C maddesi: varsa eski Pending ödeme Abandoned yapılır — Domain halleder
        var payment = reservation.StartPayment(utcNow);

        // BR-09: tutar rezervasyondan, istemciden DEĞİL
        var request = new PaymentInitRequest(
            ReservationId: reservation.Id,
            Amount: reservation.TotalAmount,
            BuyerName: buyer.FullName,
            BuyerEmail: buyer.Email,
            CallbackUrl: options.Value.CallbackUrl,
            Items: reservation.Items
                .Select(i => new PaymentBasketItem(
                    i.EventSeatId.ToString(), "Bilet", i.PriceAtReservation))
                .ToList());

        var result = await gateway.InitializeAsync(request, ct);

        if (!result.Success || result.ProviderToken is null || result.FormContent is null)
        {
            payment.MarkFailed(result.ErrorMessage ?? "Ödeme başlatılamadı.", utcNow);
            await uow.SaveChangesAsync(ct);
            throw new PaymentAppException(result.ErrorMessage ?? "Ödeme başlatılamadı.");
        }

        // Sıralama kritik: token sağlayıcıdan döndükten SONRA kaydediliyor.
        // Kayıt ile callback arasındaki dar pencere kabul edilen bir risk.
        payment.AttachProviderToken(result.ProviderToken);
        await uow.SaveChangesAsync(ct);

        return new StartPaymentResult(result.FormContent);
    }
}
