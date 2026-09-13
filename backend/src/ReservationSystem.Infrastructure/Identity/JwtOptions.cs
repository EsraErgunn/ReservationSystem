namespace ReservationSystem.Infrastructure.Identity;

/// <summary>
/// NFR-05: <see cref="Key"/> kaynak kodda değil, user-secrets / ortam değişkeni
/// üzerinden gelir. <c>appsettings.json</c>'daki karşılığı boş bırakılmıştır.
/// <para>
/// Api'de değil Infrastructure'da: hem <c>JwtTokenService</c> (üretim) hem Api'nin
/// <c>AddJwtBearer</c> yapılandırması (doğrulama) okuyor, Infrastructure ise Api'yi
/// göremez. Tek <c>Configure</c> çağrısı <c>AddInfrastructure</c> içinde.
/// </para>
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
