using ReservationSystem.Application.Payments.Models;

namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// NFR-13: ödeme sağlayıcısı iş mantığına dokunulmadan değiştirilebilmeli.
/// <c>Iyzipay</c> isim alanından tek bir tip bile buraya sızmamalı.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentInitResult> InitializeAsync(
        PaymentInitRequest request, CancellationToken ct);

    Task<PaymentVerificationResult> VerifyAsync(
        string providerToken, CancellationToken ct);

    /// <summary>E maddesi: hold süresi dolmuşken tamamlanan ödemenin iadesi.</summary>
    Task<RefundResult> RefundAsync(
        string providerPaymentId, decimal amount, CancellationToken ct);
}
