namespace ReservationSystem.Application.Payments.Models;

/// <summary>Sağlayıcıdan bağımsız ödeme başlatma isteği.</summary>
public record PaymentInitRequest(
    Guid ReservationId,
    decimal Amount,
    string BuyerName,
    string BuyerEmail,
    string CallbackUrl,
    IReadOnlyList<PaymentBasketItem> Items);

/// <summary>
/// Genel "ödeme kalemi" kavramı. iyzico sepet satırı beklediği için var, ama
/// sağlayıcıya özel değil — Stripe'a geçilse de aynı model işe yarar.
/// </summary>
public record PaymentBasketItem(string Id, string Name, decimal Price);

public record PaymentInitResult(
    bool Success,
    string? ProviderToken,
    string? FormContent,
    string? ErrorMessage);

public record PaymentVerificationResult(
    PaymentOutcome Outcome,
    string? ProviderPaymentId,
    decimal PaidAmount,
    string? FailureReason);

public enum PaymentOutcome { Succeeded, Failed, Pending }

public record RefundResult(bool Success, string? ErrorMessage);
