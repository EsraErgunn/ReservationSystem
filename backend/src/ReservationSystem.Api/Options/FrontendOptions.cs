namespace ReservationSystem.Api.Options;

/// <summary>
/// Ödeme callback'i sonrası kullanıcının yönlendirileceği istemci adresi.
/// </summary>
public class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = "http://localhost:5173";
}
