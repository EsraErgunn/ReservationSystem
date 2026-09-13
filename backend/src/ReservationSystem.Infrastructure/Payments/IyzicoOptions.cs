namespace ReservationSystem.Infrastructure.Payments;

/// <summary>
/// NFR-05: bu değerler kaynak kodda değil, user-secrets / ortam değişkeni /
/// secret store üzerinden gelir.
/// </summary>
public class IyzicoOptions
{
    public const string SectionName = "Iyzico";

    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Sandbox: https://sandbox-api.iyzipay.com</summary>
    public string BaseUrl { get; set; } = "https://sandbox-api.iyzipay.com";
}
