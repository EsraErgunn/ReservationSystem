# Kimlik Doğrulama (Auth)

**Versiyon:** 0.1
**Kapsam:** Application + Infrastructure + Api
**Dayandığı dokümanlar:** `application-katmani.md`, `api-katmani.md`, `veritabani-semasi.md`

---

## 1. Kapsam

| Var | Yok (bu sürümde) |
|---|---|
| E-posta + parola ile kayıt | Parola sıfırlama |
| Giriş → JWT access token | Refresh token |
| Rol bazlı yetki (`User` / `Admin`) | E-posta doğrulama |
| Parola hash'leme | Çok faktörlü doğrulama |
| | Harici sağlayıcı (Google vb.) |

**Refresh token neden yok:** Access token ömrü 60 dakika, hold süresi 10 dakika. Kullanıcı bir rezervasyon akışını tamamlarken token'ı süresi dolmaz. Refresh token, saklama (httpOnly cookie / rotasyon / iptal listesi) gerektiren ayrı bir problem alanı — bu projenin öğrenme hedefleri arasında değil. Gerçek e-ticaret projesinde eklenmeli, orada oturum süresi çok daha uzun olacak.

---

## 2. Portlar (Application/Abstractions)

```csharp
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    string CreateAccessToken(User user);
}
```

**İkisi de Application'da tanımlı, Infrastructure'da implemente edilir.** `System.IdentityModel.Tokens.Jwt` paketi Application'a sızmamalı.

> `ITokenService` neden `User` alıyor da `Guid userId` almıyor? Token'a rol ve e-posta claim'leri de gireceği için. Entity'yi geçmek burada makul — `User` zaten Domain tipi, Application onu zaten biliyor.

---

## 3. Kayıt

```csharp
public record RegisterCommand(string Email, string Password, string FullName)
    : ICommand<AuthResult>;

public record AuthResult(Guid UserId, string Email, string FullName, string AccessToken);
```

```csharp
public class RegisterHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenService tokens,
    IUnitOfWork uow,
    TimeProvider clock)
    : ICommandHandler<RegisterCommand, AuthResult>
{
    public async Task<AuthResult> HandleAsync(RegisterCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        if (await users.ExistsByEmailAsync(email, ct))
            throw new ConflictAppException(
                "auth.email_taken", "Bu e-posta adresi zaten kayıtlı.");

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
            // ux_users_email — iki eşzamanlı kayıt isteği yarıştı
            throw new ConflictAppException(
                "auth.email_taken", "Bu e-posta adresi zaten kayıtlı.");
        }

        return new AuthResult(user.Id, user.Email, user.FullName,
            tokens.CreateAccessToken(user));
    }
}
```

**`ExistsByEmailAsync` kontrolü varken `UniqueConstraintException` neden yakalanıyor?** Kontrol ile yazma arasında başka bir istek araya girebilir — aynı `CreateReservationHandler`'daki mantık. Uygulama kontrolü kullanıcıya *anlamlı mesaj* vermek için, veritabanı constraint'i *garanti* için.

---

## 4. Giriş

```csharp
public record LoginCommand(string Email, string Password) : ICommand<AuthResult>;

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

        // Kullanıcı yoksa da hash doğrulaması yapılır — zamanlama saldırısına karşı
        var hash = user?.PasswordHash ?? DummyHash;
        var valid = hasher.Verify(command.Password, hash);

        if (user is null || !valid)
            throw new UnauthorizedAppException();

        return new AuthResult(user.Id, user.Email, user.FullName,
            tokens.CreateAccessToken(user));
    }

    // Gerçek bir hash ile aynı maliyette sahte değer
    private const string DummyHash =
        "AQAAAAIAAYagAAAAEL...";   // bir kez üretilip sabitlenir
}
```

### İki güvenlik detayı

**A) Hata mesajı ayrım yapmıyor**
"Kullanıcı bulunamadı" ile "parola yanlış" ayrı mesajlar olursa, saldırgan hangi e-postaların kayıtlı olduğunu öğrenir (kullanıcı numaralandırma). Her iki durumda da tek mesaj: `UnauthorizedAppException`.

**B) Kullanıcı yokken de hash doğrulanıyor**
Kullanıcı bulunamayınca hemen dönersen, o istek ~1 ms sürer; parola doğrulanan istek ~100 ms sürer. Bu fark ölçülebilir ve yine kullanıcı numaralandırmaya yol açar. Sahte hash ile aynı maliyet harcanır.

> `DummyHash` sabitini bir kez `hasher.Hash("dummy")` çağırıp çıktısını koda yazarak üret. Her açılışta hesaplamak gereksiz.

---

## 5. Parola hash'leme (Infrastructure)

ASP.NET Core Identity'nin tamamını kurmadan sadece hasher'ı kullanabilirsin:

```bash
dotnet add package Microsoft.AspNetCore.Cryptography.KeyDerivation
```

```csharp
namespace ReservationSystem.Infrastructure.Identity;

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;          // OWASP 2023+ önerisi (SHA-256)
    private static readonly KeyDerivationPrf Prf = KeyDerivationPrf.HMACSHA256;

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

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);

        var actual = KeyDerivation.Pbkdf2(password, salt, Prf, iterations, expected.Length);

        // Sabit zamanlı karşılaştırma — normal == zamanlama sızdırır
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

**Neden iterasyon sayısı hash'in içinde saklanıyor:** İleride 210.000'i artırmak isteyeceksin (donanım hızlanıyor). Eski kayıtlar kendi iterasyon sayılarıyla doğrulanmaya devam eder; yeni kayıtlar yeni sayıyı kullanır. Formatı baştan böyle kurmak, sonradan migration yazmaktan kolay.

**Alternatif:** `Argon2id` (paket: `Konscious.Security.Cryptography.Argon2`) parola hash'leme için PBKDF2'den daha güçlü kabul ediliyor. Ama PBKDF2 framework içinde, ek bağımlılık yok ve doğru parametrelerle yeterli. Bu projede PBKDF2 seçildi.

> **Asla:** `MD5`, `SHA-256` gibi hızlı hash'ler parola için kullanılmaz. Hızlı olmaları tam olarak sorunun kendisi — saldırgan saniyede milyarlarca deneme yapabilir.

---

## 6. Token üretimi (Infrastructure)

```csharp
public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public string CreateAccessToken(User user)
    {
        var opt = options.Value;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(opt.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**Token'a ne konulmaz:** Parola hash'i, TC kimlik numarası, telefon — hiçbiri. JWT **imzalıdır ama şifreli değildir**; içeriği herkes okuyabilir (jwt.io'ya yapıştırmak yeterli). Sadece kimlik ve yetki için gereken minimum bilgi girer.

`ClaimTypes.Role` kullanımı, `api-katmani.md`'deki `CurrentUser.IsAdmin` implementasyonuyla (`User.IsInRole`) uyumlu olması için önemli.

---

## 7. Api uçları

```csharp
[ApiController]
[Route("api/auth")]
public class AuthController(
    ICommandHandler<RegisterCommand, AuthResult> register,
    ICommandHandler<LoginCommand, AuthResult> login)
    : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await register.HandleAsync(
            new RegisterCommand(request.Email, request.Password, request.FullName), ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
        => Ok(await login.HandleAsync(
            new LoginCommand(request.Email, request.Password), ct));

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
        => Ok(new
        {
            Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue(ClaimTypes.Email),
            Name = User.FindFirstValue(ClaimTypes.Name),
            Role = User.FindFirstValue(ClaimTypes.Role)
        });
}

public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
```

### Yeni rate limit politikası

Giriş ucu kaba kuvvet saldırısının birincil hedefi. `Program.cs`'e ekle:

```csharp
options.AddPolicy("auth", ctx =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1)
        }));
```

Burada partition **IP bazlı** — kullanıcı henüz kimlik doğrulamamış olduğu için claim yok. Bu, `UseAuthentication` sonrasında çalışsa bile geçerli: anonim isteklerde `ctx.User` boş kalır ve IP'ye düşer, ki bu doğru davranış.

---

## 8. Giriş doğrulama (validation)

Parola politikası Domain'e mi Application'a mı ait? **Application'a** — "en az 8 karakter" bir iş kuralı değil, bir giriş kısıtı. Domain `User` entity'si zaten hash alıyor, ham parolayı hiç görmüyor.

```csharp
public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalı.")
            .MaximumLength(128);

        RuleFor(x => x.FullName)
            .NotEmpty().MaximumLength(100);
    }
}
```

```bash
dotnet add package FluentValidation.AspNetCore
```

> **Karmaşıklık kuralı (büyük harf + rakam + sembol) eklenmedi** — bilinçli. NIST SP 800-63B, bu kuralların kullanıcıları tahmin edilebilir kalıplara ittiğini (`Parola123!`) ve uzunluğun karmaşıklıktan daha etkili olduğunu belirtiyor. Minimum uzunluk + maksimum sınır yeterli. Maksimum 128 karakter, hash fonksiyonuna aşırı uzun girdiyle DoS yapılmasını engellemek için.

---

## 9. Frontend tarafı

```ts
// Token saklama
localStorage.setItem('token', result.accessToken);

// Her istekte header
const api = async (path: string, init?: RequestInit) => {
  const token = localStorage.getItem('token');
  return fetch(`/api${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });
};

// SignalR — header gönderemediği için query string
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/seats', {
    accessTokenFactory: () => localStorage.getItem('token') ?? '',
  })
  .build();
```

> **`localStorage` XSS'e açık** — bunu bilerek kabul ediyoruz. Daha güvenlisi `httpOnly` cookie, ama o CSRF koruması ve refresh token rotasyonu gerektirir. Bu proje kapsamında localStorage kabul edilen bir risk; gerçek e-ticaret projesinde cookie tabanlı oturuma geçilmeli. Bu kararı README'ye yaz — bilinçli olduğu görünsün.

---

## 10. Test edilecekler

**Birim (Application):**
- Kayıtlı e-posta ile kayıt → `auth.email_taken`
- Yanlış parola ile giriş → `UnauthorizedAppException`
- Var olmayan kullanıcı ile giriş → **aynı** istisna (mesaj farkı yok)
- Üretilen token `NameIdentifier` ve `Role` claim'lerini içeriyor

**Birim (Infrastructure):**
- `Hash` aynı parola için her seferinde farklı çıktı veriyor (salt rastgele)
- `Verify` doğru parolayı kabul, yanlışı red ediyor
- Farklı iterasyon sayısıyla üretilmiş eski hash hâlâ doğrulanıyor

**Entegrasyon (Api) — Docker açıldığında:**
- Kayıt → giriş → `/api/auth/me` zinciri çalışıyor
- `/api/auth/login`'e 6. istek 429 dönüyor
- Geçersiz token ile korumalı uca istek 401 dönüyor
- Süresi dolmuş token 401 dönüyor

---

## 11. Sonraki adım

1. Auth uçlarını yaz, birim testlerini geçir
2. Docker'ı ayağa kaldır, migration'ı uygula, seed verisi üret
3. Entegrasyon testleri (`api-katmani.md` §12 + yukarıdaki auth senaryoları)
4. Swagger üzerinden uçtan uca manuel akış: kayıt → giriş → koltuk seç → ödeme başlat → callback

---

## 12. Uygulama notları (kod yazılırken çıkan sapmalar)

| Dokümandaki | Koddaki | Neden |
|---|---|---|
| `JwtOptions` Api'de (`api-katmani.md`) | `Infrastructure/Identity/JwtOptions.cs` | `JwtTokenService` (üretim) ve Api'nin `AddJwtBearer`'ı (doğrulama) aynı tipi okuyor; Infrastructure Api'yi göremez. Tek `Configure` çağrısı `AddInfrastructure` içinde |
| `FluentValidation.AspNetCore` | `FluentValidation` (Application'a) | Application ASP.NET'e bağımlı olmamalı; ayrıca otomatik doğrulama işe yaramıyor — aşağıya bakın |
| `JwtSecurityToken(expires: DateTime.UtcNow…)` | `TimeProvider` üzerinden | Kodun geri kalanıyla tutarlı; `ValidTo` deterministik olarak test edilebiliyor |
| `DummyHash = "AQAAAAIAAYagAAAAEL..."` | Gerçek PBKDF2 çıktısı | §2.1'e göre bir kez üretildi; ASP.NET Core Identity formatı (`AQAAAA…`) değil, §5'teki `{iterations}.{salt}.{key}` formatı |

### `RegisterValidator` neden handler içinde çalışıyor

`FluentValidation.AspNetCore`'un otomatik doğrulaması **bağlanan modeli** doğrular; controller'a
gelen tip `RegisterRequest`, validator ise `RegisterCommand` için yazılmış. Otomatik doğrulama
bu yüzden hiç devreye girmezdi.

Validator'ı `RegisterRequest`'e taşımak §8'in gerekçesini bozardı (parola politikası
Application'a ait, Api'ye değil). Bunun yerine `RegisterHandler` `IValidator<RegisterCommand>`
enjekte edip `ValidateAndThrowAsync` çağırıyor; `AppExceptionHandler` da
`ValidationException`'ı `validation_error` koduyla 400'e eşliyor.

### Doğrulanan davranışlar (Docker'sız duman testi)

| Kontrol | Sonuç |
|---|---|
| Swagger'da `/api/auth/register`, `/api/auth/login`, `/api/auth/me` | Üçü de görünüyor |
| Geçerli JWT ile `GET /api/auth/me` | 200, `id` / `email` / `name` / `role` claim'leri doğru |
| Token'sız ve bozuk token ile `/api/auth/me` | 401 |
| Geçersiz kayıt girdisi | 400 `validation_error`, Türkçe mesajlar |
| `POST /api/auth/login` art arda | 5 istek geçiyor, sonrası 429 — `auth` politikası IP bazlı olduğu için `register` çağrısı da aynı kotadan yiyor |

**Henüz doğrulanmadı:** kayıt → giriş → `/api/auth/me` zincirinin veritabanıyla uçtan uca
çalışması. PostgreSQL gerekiyor; `feature/integration-tests` branch'ine bırakıldı.
