using FluentValidation;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Auth;

public record RegisterCommand(string Email, string Password, string FullName)
    : ICommand<AuthResult>;

public record AuthResult(Guid UserId, string Email, string FullName, string AccessToken);

public class RegisterHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenService tokens,
    IUnitOfWork uow,
    IValidator<RegisterCommand> validator,
    TimeProvider clock)
    : ICommandHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> HandleAsync(RegisterCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var email = command.Email.Trim().ToLowerInvariant();

        if (await users.ExistsByEmailAsync(email, ct))
            throw EmailTaken();

        var user = new User(
            email,
            hasher.Hash(command.Password),
            command.FullName,
            clock.GetUtcNow().UtcDateTime);

        users.Add(user);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            // ux_users_email — iki eşzamanlı kayıt isteği yarıştı. Yukarıdaki kontrol
            // kullanıcıya *anlamlı mesaj* için, constraint *garanti* için
            // (CreateReservationHandler'daki BR-06 mantığının aynısı).
            throw EmailTaken();
        }

        return new AuthResult(user.Id, user.Email, user.FullName,
            tokens.CreateAccessToken(user));
    }

    private static ConflictAppException EmailTaken() => new(
        "auth.email_taken", "Bu e-posta adresi zaten kayıtlı.");
}
