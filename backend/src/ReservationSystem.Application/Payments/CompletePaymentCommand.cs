using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments.Models;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Application.Payments;

public record CompletePaymentCommand(string ProviderToken) : ICommand<PaymentCompletionResult>;

public record PaymentCompletionResult(Guid ReservationId, bool Confirmed, string? Message);

/// <summary>
/// Akışın en kritik handler'ı: BR-10, BR-12, D ve E maddelerinin tamamı burada.
/// </summary>
public class CompletePaymentHandler(
    IReservationRepository reservations,
    IEventSeatRepository seats,
    IPaymentGateway gateway,
    IUnitOfWork uow,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<CompletePaymentCommand, PaymentCompletionResult>
{
    public async Task<PaymentCompletionResult> HandleAsync(
        CompletePaymentCommand command, CancellationToken ct)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetByPaymentTokenAsync(command.ProviderToken, ct)
            ?? throw new NotFoundAppException("payment", command.ProviderToken);

        var payment = reservation.Payments
            .Single(p => p.ProviderToken == command.ProviderToken);

        // BR-12: aynı callback iki kez gelirse ikinci kez işleme.
        // Bu kontrol en başta — sağlayıcıya hiç gidilmeden dönülüyor.
        if (payment.Status != PaymentStatus.Pending)
            return new PaymentCompletionResult(
                reservation.Id,
                reservation.Status == ReservationStatus.Confirmed,
                "Bu ödeme zaten işlendi.");

        // BR-10: sonucu sağlayıcıya SORARAK öğren, istemciye güvenme
        var verification = await gateway.VerifyAsync(command.ProviderToken, ct);

        if (verification.Outcome != PaymentOutcome.Succeeded)
            return await HandleFailureAsync(reservation, payment, verification, utcNow, ct);

        // E maddesi: ödeme başarılı AMA hold süresi dolmuş
        if (utcNow > reservation.HeldUntil)
            return await RefundExpiredAsync(reservation, payment, verification, utcNow, ct);

        return await ConfirmAsync(reservation, payment, verification, utcNow, ct);
    }

    private async Task<PaymentCompletionResult> ConfirmAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            // D maddesi: tutar doğrulaması Domain'de yapılıyor
            payment.MarkSucceeded(
                verification.ProviderPaymentId!, verification.PaidAmount, utcNow);

            // F maddesi: Reservation + Items + EventSeats tek transaction
            reservation.Confirm(eventSeats, utcNow);

            await uow.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        await notifier.NotifySeatsChangedAsync(
            reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

        return new PaymentCompletionResult(reservation.Id, true, null);
    }

    private async Task<PaymentCompletionResult> HandleFailureAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            payment.MarkFailed(verification.FailureReason ?? "Ödeme reddedildi.", utcNow);

            // BR-11: hold serbest bırakılır
            reservation.MarkFailed(eventSeats, utcNow);

            await uow.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        await notifier.NotifySeatsChangedAsync(
            reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

        return new PaymentCompletionResult(
            reservation.Id, false, verification.FailureReason);
    }

    private async Task<PaymentCompletionResult> RefundExpiredAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        // Para çekildi ama koltuk artık bizim değil — iade şart.
        // Rezervasyon zaten temizlik görevi tarafından Expired yapılmış olabilir;
        // bu yüzden burada rezervasyon durumuna dokunulmuyor, yalnızca ödeme kapatılıyor.
        var refund = await gateway.RefundAsync(
            verification.ProviderPaymentId!, verification.PaidAmount, ct);

        payment.MarkFailed(
            refund.Success
                ? "Rezervasyon süresi doldu, ödeme iade edildi."
                : $"Rezervasyon süresi doldu, iade BAŞARISIZ: {refund.ErrorMessage}",
            utcNow);

        await uow.SaveChangesAsync(ct);

        // İade başarısızsa manuel müdahale gerekiyor. Exception fırlatmıyoruz çünkü
        // kullanıcıya durumu bildirmek gerekiyor; yüksek öncelikli log Api/Infrastructure
        // tarafında FailureReason üzerinden kurulur.

        return new PaymentCompletionResult(
            reservation.Id, false,
            "Rezervasyon süresi dolduğu için işlem tamamlanamadı. Ödemeniz iade edilecektir.");
    }
}
