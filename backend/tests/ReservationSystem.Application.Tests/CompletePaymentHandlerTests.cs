using NSubstitute;
using ReservationSystem.Application.Payments;
using ReservationSystem.Application.Payments.Models;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Application.Tests;

public class CompletePaymentHandlerTests
{
    private static CompletePaymentHandler Build(TestScenario s) => new(
        s.Reservations, s.Seats, s.Gateway, s.Uow, s.Notifier, s.Clock);

    // ---- BR-12: idempotency -------------------------------------------------

    [Fact]
    public async Task WhenAlreadyProcessed_DoesNotCallGateway()
    {
        var s = new TestScenario();
        var (_, payment) = s.GivenPendingPayment();
        payment.MarkSucceeded("iyz-1", payment.Amount, TestScenario.Now);

        var result = await Build(s).HandleAsync(new CompletePaymentCommand("tok_123"), default);

        await s.Gateway.DidNotReceive()
            .VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("Bu ödeme zaten işlendi.", result.Message);
    }

    // ---- BR-10 + D: sunucu taraflı doğrulama --------------------------------

    [Fact]
    public async Task WhenVerified_ConfirmsReservationAndSellsSeats()
    {
        var s = new TestScenario(seatCount: 2);
        var (reservation, payment) = s.GivenPendingPayment();

        s.Gateway.VerifyAsync("tok_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentVerificationResult(
                PaymentOutcome.Succeeded, "iyz-1", reservation.TotalAmount, null));

        var result = await Build(s).HandleAsync(new CompletePaymentCommand("tok_123"), default);

        Assert.True(result.Confirmed);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.All(s.EventSeats, seat => Assert.Equal(SeatStatus.Sold, seat.Status));

        await s.Notifier.Received(1).NotifySeatsChangedAsync(
            reservation.EventId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    // ---- BR-11: reddedilen ödeme hold'u serbest bırakır ---------------------

    [Fact]
    public async Task WhenDeclined_ReleasesSeats()
    {
        var s = new TestScenario(seatCount: 2);
        var (reservation, payment) = s.GivenPendingPayment();

        s.Gateway.VerifyAsync("tok_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentVerificationResult(
                PaymentOutcome.Failed, null, 0m, "insufficient_funds"));

        var result = await Build(s).HandleAsync(new CompletePaymentCommand("tok_123"), default);

        Assert.False(result.Confirmed);
        Assert.Equal("insufficient_funds", result.Message);
        Assert.Equal(ReservationStatus.Failed, reservation.Status);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.All(s.EventSeats, seat => Assert.Equal(SeatStatus.Available, seat.Status));
    }

    // ---- E maddesi: süresi dolmuş hold + başarılı ödeme = iade -------------

    [Fact]
    public async Task WhenHoldExpired_RefundsInsteadOfConfirming()
    {
        var s = new TestScenario();
        var (reservation, payment) = s.GivenPendingPayment();

        s.Gateway.VerifyAsync("tok_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentVerificationResult(
                PaymentOutcome.Succeeded, "iyz-1", reservation.TotalAmount, null));
        s.Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult(true, null));

        s.Clock.Advance(TimeSpan.FromMinutes(11));   // hold süresi 10 dakika

        var result = await Build(s).HandleAsync(new CompletePaymentCommand("tok_123"), default);

        await s.Gateway.Received(1)
            .RefundAsync("iyz-1", reservation.TotalAmount, Arg.Any<CancellationToken>());
        Assert.False(result.Confirmed);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.NotEqual(ReservationStatus.Confirmed, reservation.Status);
        Assert.All(s.EventSeats, seat => Assert.NotEqual(SeatStatus.Sold, seat.Status));
    }

    [Fact]
    public async Task WhenHoldExpiredAndRefundFails_RecordsFailureReason()
    {
        var s = new TestScenario();
        var (reservation, payment) = s.GivenPendingPayment();

        s.Gateway.VerifyAsync("tok_123", Arg.Any<CancellationToken>())
            .Returns(new PaymentVerificationResult(
                PaymentOutcome.Succeeded, "iyz-1", reservation.TotalAmount, null));
        s.Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult(false, "provider_down"));

        s.Clock.Advance(TimeSpan.FromMinutes(11));

        await Build(s).HandleAsync(new CompletePaymentCommand("tok_123"), default);

        // Manuel müdahale için iz bırakılmalı
        Assert.Contains("iade BAŞARISIZ", payment.FailureReason);
        Assert.Contains("provider_down", payment.FailureReason);
    }
}
