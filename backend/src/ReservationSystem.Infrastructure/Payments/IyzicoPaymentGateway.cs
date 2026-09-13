using System.Globalization;
using Iyzipay.Model;
using Iyzipay.Request;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Payments.Models;
using IyzipayOptions = Iyzipay.Options;

namespace ReservationSystem.Infrastructure.Payments;

/// <summary>
/// NFR-13: <c>Iyzipay</c> isim alanının izole edildiği TEK sınıf. Sağlayıcı
/// değişirse yalnızca burası yeniden yazılır; Application ve Domain'e dokunulmaz.
///
/// NFR-04: Checkout Form akışında kart bilgisi tarayıcıdan doğrudan iyzico'ya
/// gider — uygulama sunucusuna hiç ulaşmaz, dolayısıyla loglanamaz.
/// </summary>
public class IyzicoPaymentGateway(
    IOptions<IyzicoOptions> options,
    ILogger<IyzicoPaymentGateway> logger) : IPaymentGateway
{
    private static readonly string SuccessStatus = Status.SUCCESS.ToString();

    private readonly IyzipayOptions _iyzico = new()
    {
        ApiKey = options.Value.ApiKey,
        SecretKey = options.Value.SecretKey,
        BaseUrl = options.Value.BaseUrl
    };

    public async Task<PaymentInitResult> InitializeAsync(
        PaymentInitRequest request, CancellationToken ct)
    {
        var iyzicoRequest = new CreateCheckoutFormInitializeRequest
        {
            Locale = Locale.TR.ToString(),
            ConversationId = request.ReservationId.ToString(),
            Price = Money(request.Amount),
            PaidPrice = Money(request.Amount),
            Currency = Currency.TRY.ToString(),
            BasketId = request.ReservationId.ToString(),
            PaymentGroup = PaymentGroup.PRODUCT.ToString(),
            CallbackUrl = request.CallbackUrl,
            Buyer = BuildBuyer(request),
            BasketItems = request.Items.Select(BuildBasketItem).ToList()
        };

        try
        {
            var response = await CheckoutFormInitialize.Create(iyzicoRequest, _iyzico);

            if (response.Status != SuccessStatus)
            {
                logger.LogWarning(
                    "iyzico checkout form başlatılamadı. Rezervasyon={ReservationId} Kod={ErrorCode} Mesaj={ErrorMessage}",
                    request.ReservationId, response.ErrorCode, response.ErrorMessage);

                return new PaymentInitResult(false, null, null, response.ErrorMessage);
            }

            return new PaymentInitResult(
                Success: true,
                ProviderToken: response.Token,
                FormContent: response.CheckoutFormContent,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            // Ödeme henüz başlamadı; hatayı sonuca çevirmek güvenli.
            logger.LogError(ex,
                "iyzico'ya ulaşılamadı. Rezervasyon={ReservationId}", request.ReservationId);

            return new PaymentInitResult(false, null, null, "Ödeme sağlayıcısına ulaşılamadı.");
        }
    }

    /// <summary>
    /// BR-10: rezervasyonu Confirmed yapan tek kaynak. Ağ hatası burada
    /// BİLEREK yutulmaz — "doğrulayamadım" ile "ödeme başarısız" aynı şey değildir.
    /// Fırlatılan istisna ödemenin Pending kalmasını ve callback'in tekrar
    /// denenebilmesini sağlar.
    /// </summary>
    public async Task<PaymentVerificationResult> VerifyAsync(
        string providerToken, CancellationToken ct)
    {
        var response = await CheckoutForm.Retrieve(
            new RetrieveCheckoutFormRequest
            {
                Locale = Locale.TR.ToString(),
                Token = providerToken
            },
            _iyzico);

        if (response.Status != SuccessStatus)
            return new PaymentVerificationResult(
                PaymentOutcome.Failed, null, 0m, response.ErrorMessage ?? "Ödeme doğrulanamadı.");

        var outcome = response.PaymentStatus switch
        {
            "SUCCESS" => PaymentOutcome.Succeeded,
            "INIT_THREEDS" or "CALLBACK_THREEDS" or "BKM_POS_SELECTED" => PaymentOutcome.Pending,
            _ => PaymentOutcome.Failed
        };

        return new PaymentVerificationResult(
            Outcome: outcome,
            ProviderPaymentId: response.PaymentId,
            PaidAmount: ParseMoney(response.PaidPrice),
            FailureReason: outcome == PaymentOutcome.Failed
                ? response.ErrorMessage ?? response.PaymentStatus
                : null);
    }

    /// <summary>
    /// E maddesi. Tutar bazlı iade kullanılıyor çünkü elimizde <c>PaymentId</c> var;
    /// işlem bazlı iade ayrıca <c>PaymentTransactionId</c> ister.
    /// </summary>
    public async Task<RefundResult> RefundAsync(
        string providerPaymentId, decimal amount, CancellationToken ct)
    {
        try
        {
            var response = await Refund.CreateAmountBasedRefundRequest(
                new CreateAmountBasedRefundRequest
                {
                    Locale = Locale.TR.ToString(),
                    ConversationId = providerPaymentId,
                    PaymentId = providerPaymentId,
                    Price = Money(amount)
                },
                _iyzico);

            if (response.Status == SuccessStatus)
                return new RefundResult(true, null);

            // İadenin başarısız olması sistemin en kırılgan noktası — manuel inceleme gerekir.
            logger.LogCritical(
                "İADE BAŞARISIZ. PaymentId={PaymentId} Tutar={Amount} Kod={ErrorCode} Mesaj={ErrorMessage}",
                providerPaymentId, amount, response.ErrorCode, response.ErrorMessage);

            return new RefundResult(false, response.ErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "İADE BAŞARISIZ (sağlayıcıya ulaşılamadı). PaymentId={PaymentId} Tutar={Amount}",
                providerPaymentId, amount);

            return new RefundResult(false, "Ödeme sağlayıcısına ulaşılamadı.");
        }
    }

    private static Buyer BuildBuyer(PaymentInitRequest request)
    {
        var (name, surname) = SplitName(request.BuyerName);

        return new Buyer
        {
            Id = request.ReservationId.ToString(),
            Name = name,
            Surname = surname,
            Email = request.BuyerEmail,
            // iyzico bu alanları zorunlu tutuyor; kullanıcıdan toplanmadığı için
            // sandbox'ta kabul edilen yer tutucular kullanılıyor.
            IdentityNumber = "11111111111",
            RegistrationAddress = "-",
            City = "Istanbul",
            Country = "Turkey",
            Ip = "0.0.0.0"
        };
    }

    private static BasketItem BuildBasketItem(PaymentBasketItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Category1 = "Bilet",
        ItemType = BasketItemType.VIRTUAL.ToString(),
        Price = Money(item.Price)
    };

    private static (string Name, string Surname) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => ("-", "-"),
            1 => (parts[0], "-"),
            _ => (string.Join(' ', parts[..^1]), parts[^1])
        };
    }

    private static string Money(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
}
