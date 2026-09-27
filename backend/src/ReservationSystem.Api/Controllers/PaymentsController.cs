using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReservationSystem.Api.Options;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments;
using ReservationSystem.Infrastructure.Payments;

namespace ReservationSystem.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    ICommandHandler<StartPaymentCommand, StartPaymentResult> start,
    ICommandHandler<CompletePaymentCommand, PaymentCompletionResult> complete,
    IOptions<FrontendOptions> frontend,
    ILogger<PaymentsController> logger)
    : ControllerBase
{
    [HttpPost("start")]
    [Authorize]
    [EnableRateLimiting("payment")]
    [ProducesResponseType<StartPaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Start(
        StartPaymentRequest request, CancellationToken ct)
    {
        // BR-09: tutar İSTEMCİDEN ALINMIYOR — sadece rezervasyon kimliği.
        var result = await start.HandleAsync(
            new StartPaymentCommand(request.ReservationId), ct);

        return Ok(result);
    }

    /// <summary>
    /// iyzico callback'i. Tarayıcı REDIRECT'i ile gelir — JWT taşımaz, bu yüzden
    /// <see cref="AllowAnonymousAttribute"/>. Güvenlik endpoint'in korunmasından değil,
    /// Application'daki sunucu taraflı <c>VerifyAsync</c> doğrulamasından geliyor (BR-10).
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<IActionResult> Callback([FromForm] string? token, CancellationToken ct)
        => CompleteAndRedirectAsync(token, ct);

    /// <summary>
    /// Yalnızca sahte ödeme sağlayıcısı etkinken (iyzico anahtarı olmadan yerel
    /// geliştirme) vardır: test ödeme ekranındaki düğmenin sonucu sağlayıcıya
    /// yazılır, ardından akış gerçek callback ile AYNI yoldan devam eder — sunucu
    /// taraflı doğrulama (BR-10) atlanmaz.
    /// </summary>
    [HttpPost("fake-checkout")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> FakeCheckout(
        [FromForm] string? token, [FromForm] string? outcome, CancellationToken ct)
    {
        var fake = HttpContext.RequestServices.GetService<FakePaymentGateway>();
        if (fake is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(token))
            fake.TrySetOutcome(token, outcome == "success");

        return await CompleteAndRedirectAsync(token, ct);
    }

    private async Task<IActionResult> CompleteAndRedirectAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Redirect($"{frontend.Value.BaseUrl}/odeme/sonuc?durum=gecersiz");

        try
        {
            var result = await complete.HandleAsync(new CompletePaymentCommand(token), ct);

            var status = result.Confirmed ? "basarili" : "basarisiz";
            return Redirect(
                $"{frontend.Value.BaseUrl}/odeme/sonuc?durum={status}&rezervasyon={result.ReservationId}");
        }
        catch (Exception ex)
        {
            // Kullanıcıya ham hata sayfası göstermek yerine frontend'e yönlendir.
            // Ödeme Pending kaldı — PendingPaymentReconciliationService devralacak.
            logger.LogError(ex, "Ödeme callback hatası. Token: {Token}", token);
            return Redirect($"{frontend.Value.BaseUrl}/odeme/sonuc?durum=beklemede");
        }
    }
}

public record StartPaymentRequest(Guid ReservationId);
