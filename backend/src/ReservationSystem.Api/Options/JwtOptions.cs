namespace ReservationSystem.Api.Options;

/// <summary>
/// NFR-05: <see cref="Key"/> kaynak kodda değil, user-secrets / ortam değişkeni
/// üzerinden gelir. <c>appsettings.json</c>'daki karşılığı boş bırakılmıştır.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HmacSha256 için asgari uzunluk: 256 bit = 32 karakter.</summary>
    public const int MinimumKeyLength = 32;

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
