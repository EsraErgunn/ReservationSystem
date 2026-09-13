using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ReservationSystem.Application.Auth;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Infrastructure.Identity;

namespace ReservationSystem.Infrastructure.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePassword()
    {
        var first = _hasher.Hash("parola1234");
        var second = _hasher.Hash("parola1234");

        Assert.NotEqual(first, second);   // salt rastgele
        Assert.True(_hasher.Verify("parola1234", first));
        Assert.True(_hasher.Verify("parola1234", second));
    }

    [Fact]
    public void Hash_UsesConfiguredIterationsAndFitsColumn()
    {
        var hash = _hasher.Hash("parola1234");
        var parts = hash.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.Equal("210000", parts[0]);

        // users.password_hash kolonu 256 karakter
        Assert.True(hash.Length <= 256, $"hash uzunlugu {hash.Length}");
    }

    [Fact]
    public void Verify_AcceptsCorrectRejectsWrong()
    {
        var hash = _hasher.Hash("parola1234");

        Assert.True(_hasher.Verify("parola1234", hash));
        Assert.False(_hasher.Verify("parola1235", hash));
        Assert.False(_hasher.Verify("", hash));
    }

    /// <summary>
    /// Format ileriye dönük: iterasyon sayısı hash'in içinde saklandığı için, sayı
    /// ileride artırıldığında eski kayıtlar hâlâ doğrulanabilmeli.
    /// </summary>
    [Fact]
    public void Verify_AcceptsHashProducedWithDifferentIterationCount()
    {
        // 210.000 değil, daha eski/düşük bir sayıyla üretilmiş kayıt
        var legacy = LegacyHash("parola1234", iterations: 1000);

        Assert.True(_hasher.Verify("parola1234", legacy));
        Assert.False(_hasher.Verify("yanlis", legacy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bozuk")]
    [InlineData("abc.def.ghi")]
    [InlineData("210000.gecersiz-base64!.x")]
    public void Verify_MalformedHash_ReturnsFalseWithoutThrowing(string hash)
    {
        Assert.False(_hasher.Verify("parola1234", hash));
    }

    /// <summary>
    /// <c>LoginHandler.DummyHash</c> gerçek bir PBKDF2 çıktısı olmalı: bozuk olsaydı
    /// <c>Verify</c> format kontrolünde erken döner, ~100 ms yerine ~0 harcar ve
    /// zamanlama saldırısı koruması sessizce çökerdi (auth.md §4/B).
    /// </summary>
    [Fact]
    public void DummyHash_IsAWellFormedHashWithCurrentIterationCount()
    {
        var parts = LoginHandler.DummyHash.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.Equal("210000", parts[0]);
        Assert.False(_hasher.Verify("herhangi-bir-parola", LoginHandler.DummyHash));

        // Format geçerli olduğu için Verify gerçekten PBKDF2 çalıştırıyor:
        // aynı sabitten türetilen doğru parola kabul ediliyor.
        Assert.True(_hasher.Verify("dummy-placeholder", LoginHandler.DummyHash));
    }

    private static string LegacyHash(string password, int iterations)
    {
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);

        var key = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
}

public class JwtTokenServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "ReservationSystem",
        Audience = "ReservationSystem.Client",
        Key = "test-key-en-az-32-karakter-olmali-1234",
        ExpiryMinutes = 60
    };

    private static JwtSecurityToken Decode(User user)
    {
        var service = new JwtTokenService(
            Microsoft.Extensions.Options.Options.Create(Options),
            new FixedClock(Now));

        return new JwtSecurityTokenHandler().ReadJwtToken(service.CreateAccessToken(user));
    }

    [Fact]
    public void Token_CarriesIdentityClaims()
    {
        var user = new User("esra@example.com", "hash", "Esra E.", Now);

        var token = Decode(user);

        Assert.Equal(user.Id.ToString(), token.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("esra@example.com", token.Claims.First(c => c.Type == ClaimTypes.Email).Value);
        Assert.Equal("Esra E.", token.Claims.First(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal("User", token.Claims.First(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void Token_CarriesIssuerAudienceAndExpiry()
    {
        var token = Decode(new User("esra@example.com", "hash", "Esra E.", Now));

        Assert.Equal(Options.Issuer, token.Issuer);
        Assert.Contains(Options.Audience, token.Audiences);
        Assert.Equal(Now.AddMinutes(Options.ExpiryMinutes), token.ValidTo);
    }

    /// <summary>
    /// JWT imzalı ama şifreli değil — içeriği herkes okuyabilir. Parola hash'i
    /// token'a asla girmemeli (auth.md §6).
    /// </summary>
    [Fact]
    public void Token_DoesNotLeakPasswordHash()
    {
        var token = Decode(new User("esra@example.com", "gizli-hash", "Esra E.", Now));

        Assert.DoesNotContain(token.Claims, c => c.Value.Contains("gizli-hash"));
    }

    [Fact]
    public void Token_HasUniqueJtiPerCall()
    {
        var user = new User("esra@example.com", "hash", "Esra E.", Now);

        var first = Decode(user).Id;
        var second = Decode(user).Id;

        Assert.NotEqual(first, second);
    }
}

public class FixedClock(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
