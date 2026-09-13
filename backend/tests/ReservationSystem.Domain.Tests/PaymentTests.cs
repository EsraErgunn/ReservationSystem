using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Tests;

public class PaymentTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <see cref="Payment"/> ctor'u internal — ödeme yalnızca aggregate üzerinden
    /// başlatılabilir, bu yüzden test de aynı yoldan gider.
    /// </summary>
    private static (Reservation Reservation, EventSeat[] Seats, Payment Payment) BuildPendingPayment()
    {
        var @event = new Event(
            venueId: Guid.CreateVersion7(),
            title: "Konser",
            eventDate: Now.AddDays(30),
            salesStartAt: Now.AddDays(-1),
            salesEndAt: Now.AddDays(20));

        var seats = new[] { new EventSeat(@event.Id, Guid.CreateVersion7(), 250m) };
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);
        var payment = reservation.StartPayment(Now.AddMinutes(1));

        return (reservation, seats, payment);
    }

    [Fact]
    public void MarkSucceeded_WithWrongAmount_Throws()
    {
        var (_, _, payment) = BuildPendingPayment();

        var ex = Assert.Throws<DomainException>(() =>
            payment.MarkSucceeded("iyz-123", payment.Amount - 1m, Now.AddMinutes(2)));

        Assert.Equal("payment.amount_mismatch", ex.Code);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void MarkSucceeded_WithMatchingAmount_Succeeds()
    {
        var (_, _, payment) = BuildPendingPayment();
        var completedAt = Now.AddMinutes(2);

        payment.MarkSucceeded("iyz-123", payment.Amount, completedAt);

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("iyz-123", payment.ProviderPaymentId);
        Assert.Equal(completedAt, payment.CompletedAt);
    }

    [Fact]
    public void MarkSucceeded_Twice_Throws()
    {
        var (_, _, payment) = BuildPendingPayment();
        payment.MarkSucceeded("iyz-123", payment.Amount, Now.AddMinutes(2));

        // BR-12: aynı ödeme sonucu ikinci kez işlenemez
        var ex = Assert.Throws<DomainException>(() =>
            payment.MarkSucceeded("iyz-123", payment.Amount, Now.AddMinutes(3)));

        Assert.Equal("payment.not_pending", ex.Code);
    }

    [Fact]
    public void MarkFailed_RecordsReason()
    {
        var (_, _, payment) = BuildPendingPayment();

        payment.MarkFailed("insufficient_funds", Now.AddMinutes(2));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("insufficient_funds", payment.FailureReason);
    }

    [Fact]
    public void AttachProviderToken_AfterCompletion_Throws()
    {
        var (_, _, payment) = BuildPendingPayment();
        payment.MarkFailed("declined", Now.AddMinutes(2));

        var ex = Assert.Throws<DomainException>(() => payment.AttachProviderToken("tok-1"));

        Assert.Equal("payment.not_pending", ex.Code);
    }
}
