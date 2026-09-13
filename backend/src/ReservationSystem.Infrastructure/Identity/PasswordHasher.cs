using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using ReservationSystem.Application.Abstractions;

namespace ReservationSystem.Infrastructure.Identity;

/// <summary>
/// auth.md §5. ASP.NET Core Identity'nin tamamı kurulmadan yalnızca hash'leme.
/// <para>
/// MD5 / SHA-256 gibi hızlı hash'ler parola için kullanılmaz — hızlı olmaları tam
/// olarak sorunun kendisi. Argon2id daha güçlü kabul ediliyor ama ek bağımlılık
/// gerektiriyor; PBKDF2 framework içinde ve doğru parametrelerle yeterli.
/// </para>
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;          // OWASP 2023+ önerisi (SHA-256)
    private static readonly KeyDerivationPrf Prf = KeyDerivationPrf.HMACSHA256;

    /// <summary>
    /// Çıktı: <c>{iterations}.{base64 salt}.{base64 key}</c>.
    /// İterasyon sayısı hash'in içinde saklanıyor çünkü donanım hızlandıkça bu sayı
    /// artırılacak; eski kayıtlar kendi sayılarıyla doğrulanmaya devam eder ve
    /// migration yazmaya gerek kalmaz.
    /// </summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        var key = KeyDerivation.Pbkdf2(password, salt, Prf, Iterations, KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = KeyDerivation.Pbkdf2(password, salt, Prf, iterations, expected.Length);

        // Sabit zamanlı karşılaştırma — normal == zamanlama sızdırır.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
