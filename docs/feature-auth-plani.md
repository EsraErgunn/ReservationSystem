# feature/auth — Uygulama Planı

**Branch:** `feature/auth`
**Kapsam:** Kimlik doğrulama uçları + rezervasyon sorgu uçları (FR-10)
**Dayandığı dokümanlar:** `auth.md`, `api-katmani.md`, `application-katmani.md`, `veritabani-semasi.md`

---

## 1. Bu branch'te ne yapılacak

| # | İş | Dayanak | Docker gerekir mi |
|---|---|---|---|
| 1 | `IPasswordHasher` + `ITokenService` portları | `auth.md` §2 | Hayır |
| 2 | `RegisterHandler`, `LoginHandler` | `auth.md` §3–4 | Hayır |
| 3 | `PasswordHasher` (PBKDF2) | `auth.md` §5 | Hayır |
| 4 | `JwtTokenService` | `auth.md` §6 | Hayır |
| 5 | `AuthController` + `auth` rate limit politikası | `auth.md` §7 | Hayır |
| 6 | `RegisterValidator` (FluentValidation) | `auth.md` §8 | Hayır |
| 7 | Rezervasyon sorgu uçları (FR-10) | Bu doküman §3 | Hayır |
| 8 | `CreatedAtAction` sapmasının kapatılması | Bu doküman §3.5 | Hayır |
| 9 | Birim testleri | `auth.md` §10 + bu doküman §4 | Hayır |

**Kapsam dışı (sonraki branch):** Entegrasyon testleri, migration'ın veritabanına uygulanması, seed verisi, frontend.

Bu branch'in tamamı Docker olmadan yazılıp test edilebilir — bilinçli bir sıralama.

---

## 2. Auth — `auth.md`'ye ek notlar

Ana tasarım `auth.md`'de. Uygulama sırasında dikkat edilecek noktalar:

### 2.1 `DummyHash` sabitini üret

`LoginHandler` içindeki zamanlama saldırısı koruması, gerçek bir hash ile aynı maliyette sahte değer ister. Bir kez üret, koda sabit olarak yaz:

```csharp
// Tek seferlik: bir test içinde çalıştırıp çıktıyı kopyala
var hasher = new PasswordHasher();
Console.WriteLine(hasher.Hash("dummy-placeholder"));
```

Çıktı `210000.<base64>.<base64>` formatında olacak. `LoginHandler.DummyHash` sabitine yapıştır.

> Bu sabitin gerçek bir parolaya karşılık gelmesi gerekmiyor — `Verify` her zaman `false` dönecek, önemli olan **aynı süreyi harcaması**.

### 2.2 `IUserRepository`'ye eklenecek metodlar

Mevcut repository'de yoksa:

```csharp
Task<bool> ExistsByEmailAsync(string email, CancellationToken ct);
Task<User?> GetByEmailAsync(string email, CancellationToken ct);
void Add(User user);
```

`GetByEmailAsync` sorgusu `lower(email)` üzerinden gitmeli — `ux_users_email` index'i o şekilde tanımlı, aksi halde index kullanılmaz:

```csharp
// Handler zaten email'i ToLowerInvariant() ile normalize ediyor,
// entity constructor'ı da öyle kaydediyor — bu yüzden düz eşitlik yeterli.
await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
```

### 2.3 DI kayıtları

```csharp
// Infrastructure/DependencyInjection.cs
services.AddScoped<IPasswordHasher, PasswordHasher>();
services.AddScoped<ITokenService, JwtTokenService>();
services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

// Application/DependencyInjection.cs
services.AddScoped<ICommandHandler<RegisterCommand, AuthResult>, RegisterHandler>();
services.AddScoped<ICommandHandler<LoginCommand, AuthResult>, LoginHandler>();
```

> `JwtOptions` hem Api hem Infrastructure tarafında lazım. Tek yerde (Api'de) `Configure` edip Infrastructure'ın `IOptions<JwtOptions>` ile okuması yeterli — iki kez kaydetme.

### 2.4 `auth` rate limit politikası

`Program.cs`'teki `AddRateLimiter` bloğuna ekle:

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

IP bazlı — kullanıcı henüz kimlik doğrulamamış, claim yok.

---

## 3. Rezervasyon sorgu uçları (FR-10)

Henüz hiçbir dokümanda tanımlı değil. Tam tasarım burada.

### 3.1 DTO'lar

```csharp
namespace ReservationSystem.Application.Reservations.Dtos;

public record ReservationSummaryDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTime EventDate,
    string Status,
    int SeatCount,
    decimal TotalAmount,
    DateTime? HeldUntil,
    DateTime CreatedAt);

public record ReservationDetailDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTime EventDate,
    string VenueName,
    string Status,
    decimal TotalAmount,
    DateTime? HeldUntil,
    DateTime CreatedAt,
    IReadOnlyList<ReservationSeatDto> Seats,
    string? LastPaymentStatus);

public record ReservationSeatDto(
    Guid EventSeatId,
    string RowLabel,
    int SeatNumber,
    decimal Price);
```

**`HeldUntil` nullable:** Yalnızca `Held` durumunda anlamlı. `Confirmed` bir rezervasyonda geri sayım göstermek yanlış olur — okuma modelinde `null`'a çevrilir.

**`UserId` dışa çıkmıyor:** İstemcinin bilmesine gerek yok. Yetki kontrolü için gereken `UserId`, Infrastructure'ın döndürdüğü iç tipte taşınır.

### 3.2 Sorgu portu

Komut tarafındaki `IReservationRepository`'den **ayrı** — CQRS okuma tarafı, `GetSeatMapQueryHandler` ile aynı desen.

```csharp
namespace ReservationSystem.Application.Abstractions;

public interface IReservationQueries
{
    Task<IReadOnlyList<ReservationSummaryDto>> GetByUserAsync(
        Guid userId, CancellationToken ct);

    /// <summary>Yetki kontrolü için UserId taşıyan iç tip döner.</summary>
    Task<ReservationDetailWithOwnerDto?> GetDetailAsync(
        Guid id, CancellationToken ct);
}

public record ReservationDetailWithOwnerDto(
    Guid UserId,
    ReservationDetailDto Detail);
```

### 3.3 Handler'lar

```csharp
public record GetMyReservationsQuery() : IQuery<IReadOnlyList<ReservationSummaryDto>>;

public class GetMyReservationsHandler(
    IReservationQueries queries,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyReservationsQuery, IReadOnlyList<ReservationSummaryDto>>
{
    public async Task<IReadOnlyList<ReservationSummaryDto>> HandleAsync(
        GetMyReservationsQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAppException();
        return await queries.GetByUserAsync(userId, ct);
    }
}
```

```csharp
public record GetReservationByIdQuery(Guid Id) : IQuery<ReservationDetailDto>;

public class GetReservationByIdHandler(
    IReservationQueries queries,
    ICurrentUser currentUser)
    : IQueryHandler<GetReservationByIdQuery, ReservationDetailDto>
{
    public async Task<ReservationDetailDto> HandleAsync(
        GetReservationByIdQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAppException();

        var result = await queries.GetDetailAsync(query.Id, ct)
            ?? throw new NotFoundAppException("reservation", query.Id);

        // BR-15
        if (result.UserId != userId && !currentUser.IsAdmin)
            throw new ForbiddenAppException();

        return result.Detail;
    }
}
```

**Yetki kontrolü handler'da, controller'da değil.** Controller ince kalmalı; ayrıca aynı kural başka bir giriş noktasından (örneğin ileride bir GraphQL ucu) çağrılsa da geçerli olur.

### 3.4 Infrastructure implementasyonu

```csharp
public class ReservationQueries(AppDbContext context) : IReservationQueries
{
    public async Task<IReadOnlyList<ReservationSummaryDto>> GetByUserAsync(
        Guid userId, CancellationToken ct)
        => await context.Reservations
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReservationSummaryDto(
                r.Id,
                r.EventId,
                r.Event.Title,
                r.Event.EventDate,
                r.Status.ToString(),
                r.Items.Count(i => i.IsActive),
                r.TotalAmount,
                r.Status == ReservationStatus.Held ? r.HeldUntil : null,
                r.CreatedAt))
            .ToListAsync(ct);
}
```

**Dikkat:**
- `AsNoTracking()` + doğrudan projeksiyon — domain entity materyalize edilmiyor.
- `ix_reservations_user` index'i (`user_id, created_at DESC`) bu sorgu için zaten var.
- `SeatCount` yalnızca aktif satırları sayıyor.

Detay sorgusunda `LastPaymentStatus` türetimi:

```csharp
LastPaymentStatus = r.Payments
    .OrderByDescending(p => p.CreatedAt)
    .Select(p => p.Status.ToString())
    .FirstOrDefault()
```

`Reservation` üzerinde `payment_status` kolonu bilinçli olarak yok (`veritabani-semasi.md` §2.8); güncel durum okuma modelinde türetiliyor. Doğru yer burası.

### 3.5 Controller

```csharp
[HttpGet]
[ProducesResponseType<IReadOnlyList<ReservationSummaryDto>>(StatusCodes.Status200OK)]
public async Task<IActionResult> GetMine(CancellationToken ct)
    => Ok(await getMine.HandleAsync(new GetMyReservationsQuery(), ct));

[HttpGet("{id:guid}")]
[ProducesResponseType<ReservationDetailDto>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    => Ok(await getById.HandleAsync(new GetReservationByIdQuery(id), ct));
```

**Sapma kapanışı:** `GetById` artık var olduğu için `Create` metodundaki geçici `Created(...)` çağrısı asıl haline dönebilir:

```csharp
return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
```

`api-katmani.md` §14'teki sapma tablosundan bu madde silinebilir.

---

## 4. Testler

### 4.1 Application birim testleri

**Auth:**
- Kayıtlı e-posta ile kayıt → `auth.email_taken`
- `UniqueConstraintException` fırlatıldığında da `auth.email_taken` (yarış senaryosu)
- Yanlış parola ile giriş → `UnauthorizedAppException`
- Var olmayan kullanıcı ile giriş → **aynı** istisna, aynı mesaj
- Var olmayan kullanıcı senaryosunda `hasher.Verify` **çağrılıyor** (zamanlama koruması — mock ile doğrulanır)

**Rezervasyon sorguları:**
- Başkasının rezervasyonu → `ForbiddenAppException`
- Admin başkasının rezervasyonunu görebiliyor
- Var olmayan id → `NotFoundAppException`
- Kimliksiz çağrı → `UnauthorizedAppException`

### 4.2 Infrastructure birim testleri

- `Hash` aynı parola için her seferinde farklı çıktı (salt rastgele)
- `Verify` doğru parolayı kabul, yanlışı red
- Farklı iterasyon sayısıyla üretilmiş hash hâlâ doğrulanıyor (format ileriye dönük)
- `CreateAccessToken` çıktısı `NameIdentifier`, `Email`, `Role` claim'lerini içeriyor
- Üretilen token `JwtOptions.Issuer` / `Audience` değerlerini taşıyor

### 4.3 Bilinçli olarak yazılmayan test

`ReservationQueries` sorguları gerçek veritabanı ister (LINQ projeksiyonu in-memory provider'da farklı davranır). Bunlar entegrasyon testlerine bırakılıyor — sonraki branch.

---

## 5. 404 / 403 sıralaması — bilinçli karar

`GetReservationByIdHandler` önce `NotFoundAppException`, sonra `ForbiddenAppException` fırlatıyor.

Alternatif yaklaşım: başkasının kaydı için de 404 dönmek (varlığı hiç sızdırmamak). Bu daha katı ama hata ayıklamayı zorlaştırır ve kullanıcıya yanıltıcı gelir.

**Seçilen:** Önce 404, sonra 403. Kabul edilen risk: geçerli bir id tahmin eden saldırgan, 403 yanıtından o rezervasyonun var olduğunu anlar. GUID v7 tahmin edilebilir olmadığı için bu risk pratikte önemsiz.

Gerçek e-ticaret projesinde, sıralı id kullanılan bir tabloda bu karar tersine çevrilmeli.

---

## 6. Bitince kontrol listesi

- [x] `dotnet build` — 0 uyarı (TreatWarningsAsErrors açık)
- [x] `dotnet test` — 153 test geçiyor (121 mevcut + 32 yeni)
- [x] Swagger'da `/api/auth/register`, `/api/auth/login`, `/api/auth/me` görünüyor
- [x] Swagger'da `GET /api/reservations` ve `GET /api/reservations/{id}` görünüyor
- [x] `POST /api/auth/login`'e 6. istek 429 dönüyor
- [ ] Kayıt → giriş → `/api/auth/me` zinciri elle çalışıyor (Swagger'dan token yapıştırarak) — PostgreSQL gerekiyor, `feature/integration-tests`'e kaldı
- [x] `api-katmani.md` §14 sapma tablosundan `CreatedAtAction` maddesi silindi
- [x] `appsettings.json` içinde `Jwt:Key` **boş**, gerçek değer `user-secrets`'ta

---

## 7. Commit planı

```
docs: feature/auth uygulama plani ve auth tasarimi
feat(auth): parola hash'leme, jwt uretimi, kayit ve giris uclari
feat(api): rezervasyon sorgu uclari (FR-10)
test: auth ve rezervasyon sorgu birim testleri
```

Dokümanları ayrı commit'te tutmak, kod diff'ini okunabilir bırakıyor.

---

## 8. Sonraki branch

`feature/integration-tests`:
1. Docker'ı ayağa kaldır, migration'ı uygula
2. Seed verisi (1000 koltuklu salon — yük testi için de lazım olacak)
3. Testcontainers ile entegrasyon testleri (`api-katmani.md` §12 + `auth.md` §10)
4. Swagger'dan uçtan uca manuel akış: kayıt → giriş → koltuk seç → ödeme başlat → callback

---

## 9. Uygulama durumu

| # | İş | Durum |
|---|---|---|
| 1 | `IPasswordHasher` + `ITokenService` portları | ✅ |
| 2 | `RegisterHandler`, `LoginHandler` | ✅ |
| 3 | `PasswordHasher` (PBKDF2, 210k iterasyon) | ✅ |
| 4 | `JwtTokenService` | ✅ |
| 5 | `AuthController` + `auth` rate limit politikası | ✅ |
| 6 | `RegisterValidator` | ✅ |
| 7 | Rezervasyon sorgu uçları (FR-10) | ✅ |
| 8 | `CreatedAtAction` sapmasının kapatılması | ✅ |
| 9 | Birim testleri | ✅ 153 test geçiyor (121 → 153) |

Uygulama sırasında çıkan sapmalar `auth.md` §12'de; FR-10 tarafındaki tek sapma aşağıda.

§3'ün uygulamasında tek sapma: plandaki `r.Event.Title` projeksiyonu. `Reservation`
üzerinde `Event` navigasyon özelliği yok (`ReservationConfiguration` ilişkiyi
`HasOne<Event>().WithMany()` ile gölge olarak kuruyor, domain modeli temiz kalsın diye).
Bu yüzden sorgu `GetSeatMapQueryHandler` ile aynı desende açık `Join` kullanıyor.
