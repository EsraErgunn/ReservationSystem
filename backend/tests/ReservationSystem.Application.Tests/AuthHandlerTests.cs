using FluentValidation;
using NSubstitute;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Auth;
using ReservationSystem.Application.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Tests;

public class RegisterHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RegisterHandler Build() => new(
        _users, _hasher, _tokens, _uow, new RegisterValidator(), new FixedClock(Now));

    private static RegisterCommand Command(string email = "Esra@Example.com") =>
        new(email, "parola1234", "Esra E.");

    public RegisterHandlerTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
        _tokens.CreateAccessToken(Arg.Any<User>()).Returns("token");
    }

    [Fact]
    public async Task CreatesUserWithNormalizedEmailAndHashedPassword()
    {
        _users.ExistsByEmailAsync("esra@example.com", Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await Build().HandleAsync(Command(), default);

        Assert.Equal("esra@example.com", result.Email);
        Assert.Equal("token", result.AccessToken);

        _users.Received(1).Add(Arg.Is<User>(u =>
            u.Email == "esra@example.com" && u.PasswordHash == "hashed"));

        // Ham parola asla kaydedilmemeli
        _hasher.Received(1).Hash("parola1234");
    }

    [Fact]
    public async Task ExistingEmail_Throws()
    {
        _users.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() =>
            Build().HandleAsync(Command(), default));

        Assert.Equal("auth.email_taken", ex.Code);
        _users.DidNotReceive().Add(Arg.Any<User>());
    }

    /// <summary>
    /// Yarış senaryosu: kontrol ile yazma arasında başka bir istek araya girdi.
    /// Kullanıcı yine anlamlı mesajı görmeli, 500 değil.
    /// </summary>
    [Fact]
    public async Task ConcurrentRegistration_MapsUniqueViolationToSameError()
    {
        _users.ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintException("ux_users_email"));

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() =>
            Build().HandleAsync(Command(), default));

        Assert.Equal("auth.email_taken", ex.Code);
    }

    [Theory]
    [InlineData("gecersiz-eposta", "parola1234", "Esra E.")]
    [InlineData("esra@example.com", "kisa", "Esra E.")]
    [InlineData("esra@example.com", "parola1234", "")]
    public async Task InvalidInput_Throws(string email, string password, string fullName)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Build().HandleAsync(new RegisterCommand(email, password, fullName), default));

        _users.DidNotReceive().Add(Arg.Any<User>());
    }
}

public class LoginHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();

    private LoginHandler Build() => new(_users, _hasher, _tokens);

    private static User Existing() => new("esra@example.com", "stored-hash", "Esra E.", Now);

    [Fact]
    public async Task CorrectPassword_ReturnsToken()
    {
        var user = Existing();

        _users.GetByEmailAsync("esra@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _hasher.Verify("parola1234", "stored-hash").Returns(true);
        _tokens.CreateAccessToken(user).Returns("token");

        var result = await Build().HandleAsync(
            new LoginCommand("Esra@Example.com", "parola1234"), default);

        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("token", result.AccessToken);
    }

    [Fact]
    public async Task WrongPassword_Throws()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Existing());
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build().HandleAsync(new LoginCommand("esra@example.com", "yanlis"), default));
    }

    /// <summary>
    /// Kullanıcı numaralandırma koruması: var olmayan kullanıcı ile yanlış parola
    /// AYNI istisnayı ve AYNI mesajı vermeli.
    /// </summary>
    [Fact]
    public async Task UnknownUser_ThrowsSameExceptionAndMessageAsWrongPassword()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var unknown = await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build().HandleAsync(new LoginCommand("yok@example.com", "parola1234"), default));

        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Existing());

        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build().HandleAsync(new LoginCommand("esra@example.com", "yanlis"), default));

        Assert.Equal(wrongPassword.Code, unknown.Code);
        Assert.Equal(wrongPassword.Message, unknown.Message);
    }

    /// <summary>
    /// Zamanlama saldırısı koruması: kullanıcı yokken de hash doğrulaması yapılmalı.
    /// Erken dönmek isteği ~1 ms'ye düşürür ve e-postanın kayıtlı olmadığını sızdırır.
    /// </summary>
    [Fact]
    public async Task UnknownUser_StillVerifiesAgainstDummyHash()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build().HandleAsync(new LoginCommand("yok@example.com", "parola1234"), default));

        _hasher.Received(1).Verify("parola1234", Arg.Any<string>());
    }

    [Fact]
    public async Task UnknownUser_DoesNotIssueToken()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        // Sahte hash'e karşı doğrulama tesadüfen true dönse bile token verilmemeli.
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build().HandleAsync(new LoginCommand("yok@example.com", "parola1234"), default));

        _tokens.DidNotReceive().CreateAccessToken(Arg.Any<User>());
    }
}
