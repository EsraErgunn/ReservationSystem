namespace ReservationSystem.Application.Payments;

/// <summary>
/// NFR-05: anahtarlar burada tutulmaz. Bu yalnızca akışın ihtiyaç duyduğu
/// sağlayıcıdan bağımsız ayar; sağlayıcı kimlik bilgileri Infrastructure'ın
/// kendi options sınıfına aittir.
/// </summary>
public class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>Sağlayıcının ödeme sonrası döneceği uygulama adresi.</summary>
    public string CallbackUrl { get; set; } = string.Empty;
}
