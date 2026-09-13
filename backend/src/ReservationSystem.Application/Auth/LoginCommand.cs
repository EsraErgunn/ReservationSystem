using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Auth;

public record LoginCommand(string Email, string Password) : ICommand<AuthResult>;

/// <summary>
/// auth.md §4. İki güvenlik detayı burada:
/// <list type="bullet">
/// <item>Hata mesajı "kullanıcı yok" ile "parola yanlış" arasında ayrım yapmaz —
/// aksi halde saldırgan hangi e-postaların kayıtlı olduğunu öğrenir.</item>
/// <item>Kullanıcı bulunamadığında da hash doğrulanır: erken dönmek isteği ~1 ms'ye
/// düşürür, gerçek doğrulama ~100 ms sürer. Bu fark ölçülebilir ve yine kullanıcı
/// numaralandırmaya yol açar.</item>
/// </list>
/// </summary>
public class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenService tokens)
    : ICommandHandler<LoginCommand, AuthResult>
{
    public async Task<AuthResult> HandleAsync(LoginCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(email, ct);

        // Kullanıcı yoksa da hash doğrulaması yapılır — zamanlama saldırısına karşı.
        var hash = user?.PasswordHash ?? DummyHash;
        var valid = hasher.Verify(command.Password, hash);

        if (user is null || !valid)
            throw new UnauthorizedAppException();

        return new AuthResult(user.Id, user.Email, user.FullName,
            tokens.CreateAccessToken(user));
    }

    /// <summary>
    /// Gerçek bir hash ile aynı maliyette sahte değer. Bir kez üretilip sabitlendi —
    /// her açılışta hesaplamak gereksiz. Karşılık geldiği parolanın önemi yok;
    /// <c>Verify</c> her zaman <c>false</c> dönecek, önemli olan aynı süreyi harcaması.
    /// </summary>
    internal const string DummyHash =
        "210000.zafu0X6jF+3Av7jyGKMEEw==.bIO7oUEQ8cwILRJRT9FIBWWC5/+5uqFxaH8KWGiUfVI=";
}
