using Microsoft.Extensions.Logging.Abstractions;
using ReservationSystem.Application.Payments.Models;
using ReservationSystem.Infrastructure.Payments;

namespace ReservationSystem.Infrastructure.Tests;

public class FakePaymentGatewayTests
{
    private readonly FakePaymentGateway _gateway = new(NullLogger<FakePaymentGateway>.Instance);

    private static PaymentInitRequest Request(decimal amount = 500m, string callback = "") => new(
        Guid.CreateVersion7(), amount, "Esra E.", "esra@example.com", callback,
        [new PaymentBasketItem("1", "Bilet", amount)]);

    [Fact]
    public async Task Initialize_ReturnsTokenAndFormPostingToFakeCheckout()
    {
        var result = await _gateway.InitializeAsync(
            Request(callback: "http://localhost:8080/api/payments/callback"), default);

        Assert.True(result.Success);
        Assert.StartsWith("fake_", result.ProviderToken);
        Assert.Contains("http://localhost:8080/api/payments/fake-checkout", result.FormContent);
        Assert.Contains(result.ProviderToken!, result.FormContent);
    }

    [Theory]
    [InlineData("", "/api/payments/fake-checkout")]
    [InlineData("https://x.ngrok.app/api/payments/callback/", "https://x.ngrok.app/api/payments/fake-checkout")]
    public void FakeCheckoutUrl_DerivedFromCallback(string callback, string expected)
        => Assert.Equal(expected, FakePaymentGateway.FakeCheckoutUrl(callback));

    [Fact]
    public async Task Verify_BeforeOutcome_IsPending()
    {
        var init = await _gateway.InitializeAsync(Request(), default);

        var result = await _gateway.VerifyAsync(init.ProviderToken!, default);

        Assert.Equal(PaymentOutcome.Pending, result.Outcome);
    }

    [Fact]
    public async Task Verify_AfterSuccess_ReturnsPaidAmount()
    {
        var init = await _gateway.InitializeAsync(Request(750m), default);
        Assert.True(_gateway.TrySetOutcome(init.ProviderToken!, succeeded: true));

        var result = await _gateway.VerifyAsync(init.ProviderToken!, default);

        Assert.Equal(PaymentOutcome.Succeeded, result.Outcome);
        Assert.Equal(750m, result.PaidAmount);
        Assert.NotNull(result.ProviderPaymentId);
    }

    [Fact]
    public async Task Verify_AfterDecline_Fails()
    {
        var init = await _gateway.InitializeAsync(Request(), default);
        _gateway.TrySetOutcome(init.ProviderToken!, succeeded: false);

        var result = await _gateway.VerifyAsync(init.ProviderToken!, default);

        Assert.Equal(PaymentOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task TrySetOutcome_IsWriteOnce()
    {
        var init = await _gateway.InitializeAsync(Request(), default);

        Assert.True(_gateway.TrySetOutcome(init.ProviderToken!, succeeded: false));
        Assert.False(_gateway.TrySetOutcome(init.ProviderToken!, succeeded: true));
        Assert.False(_gateway.TrySetOutcome("fake_unknown", succeeded: true));
    }

    [Fact]
    public async Task Verify_UnknownToken_Fails()
    {
        var result = await _gateway.VerifyAsync("fake_unknown", default);

        Assert.Equal(PaymentOutcome.Failed, result.Outcome);
    }
}
