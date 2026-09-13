using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Entities;

public class Payment : Entity
{
    public Guid ReservationId { get; private set; }
    public string? ProviderToken { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private Payment() { }   // EF Core için

    internal Payment(Guid reservationId, decimal amount, DateTime utcNow)
    {
        if (amount <= 0)
            throw new DomainException("payment.invalid_amount", "Tutar sıfırdan büyük olmalı.");

        ReservationId = reservationId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = utcNow;
    }

    public void AttachProviderToken(string token)
    {
        EnsurePending();
        ProviderToken = token;
    }

    /// <summary>BR-09: sağlayıcıdan dönen tutar sunucu tarafında doğrulanır.</summary>
    public void MarkSucceeded(string providerPaymentId, decimal paidAmount, DateTime utcNow)
    {
        EnsurePending();

        if (paidAmount != Amount)
            throw new DomainException(
                "payment.amount_mismatch",
                "Ödenen tutar beklenen tutarla eşleşmiyor.");

        ProviderPaymentId = providerPaymentId;
        Status = PaymentStatus.Succeeded;
        CompletedAt = utcNow;
    }

    public void MarkFailed(string reason, DateTime utcNow)
    {
        EnsurePending();
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        CompletedAt = utcNow;
    }

    internal void Abandon(DateTime utcNow)
    {
        EnsurePending();
        Status = PaymentStatus.Abandoned;
        CompletedAt = utcNow;
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.Pending)
            throw new DomainException(
                "payment.not_pending",
                "Tamamlanmış bir ödeme değiştirilemez.");
    }
}
