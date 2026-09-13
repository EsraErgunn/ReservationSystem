using NSubstitute;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments;
using ReservationSystem.Application.Payments.Models;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Application.Tests;

public class StartPaymentHandlerTests
{
    private static StartPaymentHandler Build(TestScenario s) => new(
        s.Reservations, s.Users, s.Gateway, s.Uow, s.CurrentUser, s.Clock, s.PaymentOptions);

    private static PaymentInitResult Ok(string token) =>
        new(Success: true, ProviderToken: token, FormContent: "<form/>", ErrorMessage: null);

    // ---- BR-13: sadece kendi rezervasyonun ---------------------------------

    [Fact]
    public async Task ForAnotherUsersReservation_Throws()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.CurrentUser.UserId.Returns(Guid.CreateVersion7());   // başkası

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Build(s).HandleAsync(new StartPaymentCommand(reservation.Id), default));

        await s.Gateway.DidNotReceive()
            .InitializeAsync(Arg.Any<PaymentInitRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenNotAuthenticated_Throws()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.CurrentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build(s).HandleAsync(new StartPaymentCommand(reservation.Id), default));
    }

    // ---- BR-09: tutar sunucuda hesaplanır -----------------------------------

    [Fact]
    public async Task SendsServerCalculatedAmountAndBasket()
    {
        var s = new TestScenario(seatCount: 2);
        var reservation = s.GivenHeldReservation();

        s.Gateway.InitializeAsync(Arg.Any<PaymentInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(Ok("tok_1"));

        var result = await Build(s).HandleAsync(new StartPaymentCommand(reservation.Id), default);

        Assert.Equal("<form/>", result.FormContent);

        await s.Gateway.Received(1).InitializeAsync(
            Arg.Is<PaymentInitRequest>(r =>
                r.Amount == reservation.TotalAmount
                && r.Items.Count == 2
                && r.BuyerEmail == "esra@example.com"
                && r.CallbackUrl == "https://localhost/payments/callback"),
            Arg.Any<CancellationToken>());
    }

    // ---- C maddesi: ikinci başlatma eskisini Abandoned yapar ---------------

    [Fact]
    public async Task SecondCall_AbandonsFirstPayment()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.Gateway.InitializeAsync(Arg.Any<PaymentInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(Ok("tok_1"), Ok("tok_2"));

        var handler = Build(s);
        await handler.HandleAsync(new StartPaymentCommand(reservation.Id), default);
        await handler.HandleAsync(new StartPaymentCommand(reservation.Id), default);

        var payments = reservation.Payments.ToList();
        Assert.Equal(2, payments.Count);
        Assert.Equal(PaymentStatus.Abandoned, payments[0].Status);
        Assert.Equal(PaymentStatus.Pending, payments[1].Status);
        Assert.Equal("tok_2", payments[1].ProviderToken);
    }

    // ---- Sağlayıcı hatası ---------------------------------------------------

    [Fact]
    public async Task WhenGatewayFails_MarksPaymentFailedAndThrows()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.Gateway.InitializeAsync(Arg.Any<PaymentInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentInitResult(false, null, null, "provider_down"));

        var ex = await Assert.ThrowsAsync<PaymentAppException>(() =>
            Build(s).HandleAsync(new StartPaymentCommand(reservation.Id), default));

        Assert.Equal("payment_error", ex.Code);
        Assert.Equal(PaymentStatus.Failed, reservation.Payments.Single().Status);
        await s.Uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenHoldExpired_Throws()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.Clock.Advance(TimeSpan.FromMinutes(11));

        var ex = await Assert.ThrowsAsync<Domain.Common.DomainException>(() =>
            Build(s).HandleAsync(new StartPaymentCommand(reservation.Id), default));

        Assert.Equal("reservation.expired", ex.Code);
    }
}
