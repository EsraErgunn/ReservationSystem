using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Payments.Models;

namespace ReservationSystem.Infrastructure.Payments;

/// <summary>
/// Yalnızca yerel geliştirme ve demo için: iyzico anahtarı olmadan uçtan uca ödeme
/// akışını (başlat → sağlayıcı formu → callback → sunucu taraflı doğrulama) çalıştırır.
/// <para>
/// NFR-13'ün pratik kanıtı: Application'daki hiçbir handler bu sınıfın varlığından
/// haberdar değil; <see cref="IPaymentGateway"/> kaydını değiştirmek yeterli.
/// </para>
/// Durum bellek içinde tutulur — API yeniden başlarsa bekleyen sahte ödemeler kaybolur.
/// </summary>
public class FakePaymentGateway(ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, FakeCheckout> _checkouts = new();

    private sealed record FakeCheckout(decimal Amount, bool? Succeeded);

    public Task<PaymentInitResult> InitializeAsync(PaymentInitRequest request, CancellationToken ct)
    {
        var token = $"fake_{Guid.CreateVersion7():N}";
        _checkouts[token] = new FakeCheckout(request.Amount, null);

        logger.LogWarning(
            "SAHTE ödeme sağlayıcısı kullanılıyor. Rezervasyon={ReservationId} Tutar={Amount}",
            request.ReservationId, request.Amount);

        return Task.FromResult(new PaymentInitResult(
            Success: true,
            ProviderToken: token,
            FormContent: BuildForm(token, request),
            ErrorMessage: null));
    }

    /// <summary>Sahte ödeme ekranındaki düğmenin sonucu — gerçek sağlayıcıda kart işleminin yerini tutar.</summary>
    public bool TrySetOutcome(string token, bool succeeded)
    {
        if (!_checkouts.TryGetValue(token, out var checkout) || checkout.Succeeded is not null)
            return false;

        return _checkouts.TryUpdate(token, checkout with { Succeeded = succeeded }, checkout);
    }

    public Task<PaymentVerificationResult> VerifyAsync(string providerToken, CancellationToken ct)
    {
        if (!_checkouts.TryGetValue(providerToken, out var checkout))
            return Task.FromResult(new PaymentVerificationResult(
                PaymentOutcome.Failed, null, 0m, "Ödeme kaydı bulunamadı (sahte sağlayıcı yeniden başlatılmış olabilir)."));

        var result = checkout.Succeeded switch
        {
            null => new PaymentVerificationResult(PaymentOutcome.Pending, null, 0m, null),
            true => new PaymentVerificationResult(
                PaymentOutcome.Succeeded, $"fakepay_{providerToken[5..]}", checkout.Amount, null),
            false => new PaymentVerificationResult(
                PaymentOutcome.Failed, null, 0m, "Kart reddedildi (sahte sağlayıcı).")
        };

        return Task.FromResult(result);
    }

    public Task<RefundResult> RefundAsync(string providerPaymentId, decimal amount, CancellationToken ct)
    {
        logger.LogWarning("SAHTE iade. PaymentId={PaymentId} Tutar={Amount}", providerPaymentId, amount);
        return Task.FromResult(new RefundResult(true, null));
    }

    /// <summary>
    /// iyzico'nun Checkout Form içeriği gibi, istemcinin sayfaya gömdüğü HTML.
    /// Form, callback adresinin yanındaki <c>fake-checkout</c> ucuna gönderilir.
    /// </summary>
    private static string BuildForm(string token, PaymentInitRequest request)
    {
        var action = FakeCheckoutUrl(request.CallbackUrl);
        var amount = request.Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));

        return $"""
            <div class="fake-checkout">
              <p><strong>Test ödeme ekranı</strong> — gerçek kart bilgisi istenmez.</p>
              <p>Ödenecek tutar: <strong>{WebUtility.HtmlEncode(amount)} TL</strong></p>
              <form method="post" action="{WebUtility.HtmlEncode(action)}">
                <input type="hidden" name="token" value="{token}" />
                <button type="submit" name="outcome" value="success" class="btn btn-primary">Ödemeyi onayla</button>
                <button type="submit" name="outcome" value="fail" class="btn btn-danger">Kartı reddet</button>
              </form>
            </div>
            """;
    }

    internal static string FakeCheckoutUrl(string callbackUrl)
    {
        var trimmed = callbackUrl.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? "/api/payments/fake-checkout" : trimmed[..slash] + "/fake-checkout";
    }
}
