# Api Katmanı

**Versiyon:** 0.1
**Dayandığı dokümanlar:** `gereksinim-dokumani.md`, `application-katmani.md`, `veritabani-semasi.md`

---

## 1. Bu katmanın işi

HTTP dünyası ile Application katmanı arasındaki çeviri. Controller'lar **ince** olmalı: isteği al, handler'ı çağır, sonucu döndür. İş mantığı yok, veritabanı erişimi yok, `if` bloğu neredeyse yok.

Bir controller metodunda 10 satırdan fazla kod varsa, muhtemelen Application'a ait bir şey buraya sızmış demektir.

---

## 2. Paketler

```bash
cd backend/src/ReservationSystem.Api

dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Swashbuckle.AspNetCore
dotnet add package AspNetCore.HealthChecks.NpgSql
dotnet add package AspNetCore.HealthChecks.Redis
```

Rate limiting için ek paket gerekmiyor — `Microsoft.AspNetCore.RateLimiting` .NET 7'den beri framework içinde.

---

## 3. İstisna → HTTP eşlemesi

.NET 8+ ile gelen `IExceptionHandler` arayüzü, eski `UseExceptionHandler` middleware'inden daha temiz.

```csharp
namespace ReservationSystem.Api.Handlers;

public class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, detail) = Map(exception);

        if (status >= 500)
            logger.LogError(exception, "İşlenmeyen hata: {Code}", code);
        else
            logger.LogWarning("İş hatası: {Code} — {Detail}", code, detail);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }

    private static (int Status, string Code, string Detail) Map(Exception ex) => ex switch
    {
        NotFoundAppException e      => (404, e.Code, e.Message),
        ConflictAppException e      => (409, e.Code, e.Message),
        ConcurrencyConflictException e => (409, e.Code, e.Message),
        ForbiddenAppException e     => (403, e.Code, e.Message),
        UnauthorizedAppException e  => (401, e.Code, e.Message),
        PaymentAppException e       => (422, e.Code, e.Message),
        DomainException e           => (400, e.Code, e.Message),

        // Bilinmeyen hata — iç detay SIZDIRILMAZ
        _ => (500, "internal_error", "Beklenmeyen bir hata oluştu.")
    };
}
```

**Dikkat:** Son satır kritik. Bilinmeyen istisnanın `ex.Message`'ını kullanıcıya döndürmek, veritabanı bağlantı dizesi veya dosya yolu gibi iç detayları sızdırabilir. Log'a tam hali gider, kullanıcıya genel mesaj (NFR-08 ruhu).

`traceId` sayesinde kullanıcı "hata aldım" dediğinde loglardan tam o isteği bulabilirsin (NFR-09).

---

## 4. `ICurrentUser` implementasyonu

Application'ın tanımladığı port, burada HTTP context'ten besleniyor.

```csharp
namespace ReservationSystem.Api.Services;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var claim = accessor.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool IsAdmin =>
        accessor.HttpContext?.User.IsInRole(nameof(UserRole.Admin)) ?? false;
}
```

> **Neden Api'de, Infrastructure'da değil?** `HttpContext` bir web kavramı. Infrastructure'a koymak, arka plan servisinin (HTTP isteği olmayan) bu sınıfa bağımlı hale gelmesi riskini doğurur.

---

## 5. `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// ---------- Loglama ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---------- Katmanlar ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---------- HTTP ----------
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// ---------- Kimlik doğrulama ----------
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR WebSocket üzerinden Authorization header gönderemez
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ---------- CORS ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()!)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());   // SignalR için şart
});

// ---------- Rate limiting (NFR-06) ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("reservation", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? ctx.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("payment", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? ctx.Connection.RemoteIpAddress?.ToString()
                          ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// ---------- Sağlık kontrolü (NFR-11) ----------
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres")
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis");

var app = builder.Build();

// ---------- Pipeline — SIRA ÖNEMLİ ----------
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SeatHub>("/hubs/seats");
app.MapHealthChecks("/health");

app.Run();
```

### Pipeline sırası neden önemli

`UseExceptionHandler` **en üstte** olmalı — altındaki her şeyin hatasını yakalayabilmesi için.

`UseCors`, `UseAuthentication`'dan **önce** gelmeli. Tersi durumda tarayıcının preflight (`OPTIONS`) isteği kimlik doğrulamaya takılır ve CORS hatası alırsın — hata mesajı yanıltıcıdır, saatler kaybettirir.

`UseAuthentication` → `UseAuthorization` sırası sabittir: önce "sen kimsin", sonra "yetkin var mı".

`UseRateLimiter` bu ikisinin **arasında** olmalı. `UseAuthentication`'dan önce koyarsan `ctx.User` henüz boştur ve `partitionKey` daima IP adresine düşer — kullanıcı bazlı kota hiç devreye girmez, aynı NAT arkasındaki tüm kullanıcılar tek kotayı paylaşır. `UseAuthorization`'dan önce olması ise kimliksiz isteklerin de limite tabi kalmasını sağlar (401'e takılıp limiti atlamasınlar).

---

## 6. Controller'lar

### 6.1 `ReservationsController`

```csharp
[ApiController]
[Route("api/reservations")]
[Authorize]                        // BR-14
public class ReservationsController(
    ICommandHandler<CreateReservationCommand, ReservationDto> create,
    ICommandHandler<CancelReservationCommand, Unit> cancel)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("reservation")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateReservationRequest request, CancellationToken ct)
    {
        var result = await create.HandleAsync(
            new CreateReservationCommand(request.EventId, request.EventSeatIds), ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await cancel.HandleAsync(new CancelReservationCommand(id), ct);
        return NoContent();
    }
}

public record CreateReservationRequest(Guid EventId, IReadOnlyList<Guid> EventSeatIds);
```

**Controller'ın yaptığı tek şey:** DTO'yu command'a çevirip handler'ı çağırmak. Kural kontrolü yok — `409 Conflict` dönüşü bile `AppExceptionHandler` tarafından otomatik yapılıyor.

### 6.2 `PaymentsController` — en dikkat gerektiren yer

```csharp
[ApiController]
[Route("api/payments")]
public class PaymentsController(
    ICommandHandler<StartPaymentCommand, StartPaymentResult> start,
    ICommandHandler<CompletePaymentCommand, PaymentCompletionResult> complete,
    IOptions<FrontendOptions> frontend,
    ILogger<PaymentsController> logger)
    : ControllerBase
{
    [HttpPost("start")]
    [Authorize]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Start(
        StartPaymentRequest request, CancellationToken ct)
    {
        // BR-09: tutar İSTEMCİDEN ALINMIYOR — sadece rezervasyon kimliği
        var result = await start.HandleAsync(
            new StartPaymentCommand(request.ReservationId), ct);

        return Ok(result);
    }

    /// <summary>
    /// iyzico callback'i. Tarayıcı REDIRECT'i ile gelir — JWT taşımaz.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Callback(
        [FromForm] string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Redirect($"{frontend.Value.BaseUrl}/odeme/sonuc?durum=gecersiz");

        try
        {
            var result = await complete.HandleAsync(
                new CompletePaymentCommand(token), ct);

            var status = result.Confirmed ? "basarili" : "basarisiz";
            return Redirect(
                $"{frontend.Value.BaseUrl}/odeme/sonuc?durum={status}&rezervasyon={result.ReservationId}");
        }
        catch (Exception ex)
        {
            // Kullanıcıya ham hata sayfası göstermek yerine frontend'e yönlendir.
            // Ödeme Pending kaldı — mutabakat servisi devralacak (Bölüm 8).
            logger.LogError(ex, "Ödeme callback hatası. Token: {Token}", token);
            return Redirect($"{frontend.Value.BaseUrl}/odeme/sonuc?durum=beklemede");
        }
    }
}

public record StartPaymentRequest(Guid ReservationId);
```

### Bu controller'daki dört kritik nokta

**A) Callback `[AllowAnonymous]` olmak zorunda**
iyzico, kullanıcının tarayıcısını bu URL'e yönlendirir. Tarayıcı `Authorization` header'ı taşımaz. Kimlik doğrulama koyarsan hiçbir ödeme tamamlanamaz.

**B) Yetkisiz olması güvenlik açığı yaratmıyor**
Endpoint kimliksiz erişilebilir ama **hiçbir şey yapmıyor** — sadece token'ı Application'a iletiyor. Orada `VerifyAsync` ile sağlayıcıya sorularak sonuç öğreniliyor (BR-10). Saldırgan uydurma token gönderse `VerifyAsync` başarısız olur. Güvenlik, endpoint'in korunmasından değil, **sunucu taraflı doğrulamadan** geliyor.

**C) `[FromForm]`, `[FromBody]` değil**
iyzico `application/x-www-form-urlencoded` ile POST eder. `[FromBody]` yazarsan token hep `null` gelir ve sebebini bulmak zaman alır.

**D) Hata durumunda bile redirect**
Exception fırlatıp 500 dönmek, kullanıcıya çirkin bir hata sayfası gösterir. Bunun yerine frontend'e "beklemede" durumuyla yönlendiriliyor. Ödeme `Pending` kaldığı için mutabakat servisi devralacak.

### 6.3 `EventsController`

```csharp
[ApiController]
[Route("api/events")]
public class EventsController(
    IQueryHandler<GetSeatMapQuery, SeatMapDto> seatMap)
    : ControllerBase
{
    [HttpGet("{id:guid}/seats")]
    [AllowAnonymous]                          // Ziyaretçi de görebilir
    [ResponseCache(Duration = 5)]
    public async Task<IActionResult> GetSeatMap(Guid id, CancellationToken ct)
        => Ok(await seatMap.HandleAsync(new GetSeatMapQuery(id), ct));
}
```

> `ResponseCache` 5 saniyelik — koltuk durumu hızlı değişiyor, uzun cache yanlış bilgi gösterir. Asıl güncellik SignalR'dan geliyor; bu cache sadece sayfa ilk açılışındaki yükü azaltıyor.

---

## 7. SignalR hub

```csharp
namespace ReservationSystem.Infrastructure.RealTime;

public class SeatHub : Hub
{
    public async Task JoinEvent(Guid eventId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"event:{eventId}");

    public async Task LeaveEvent(Guid eventId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"event:{eventId}");
}
```

Hub `[Authorize]` **değil** — koltuk doluluğunu ziyaretçi de görebilmeli (FR-03). Yayınlanan veri hassas değil: hangi koltuk dolu, o kadar. Kimin aldığı bilgisi gönderilmiyor.

Frontend bağlantısı:

```ts
const connection = new HubConnectionBuilder()
  .withUrl(`/hubs/seats`)          // Vite proxy üzerinden
  .withAutomaticReconnect()
  .build();

await connection.start();
await connection.invoke('JoinEvent', eventId);

connection.on('SeatsChanged', (seatIds: string[]) => {
  queryClient.invalidateQueries({ queryKey: ['seatMap', eventId] });
});
```

> Bildirimde koltukların **yeni durumunu** göndermek yerine sadece kimliklerini gönderip istemciye yeniden sorgulatmak daha güvenli: yarış durumunda istemcinin elindeki veri her zaman sunucudan doğrulanmış olur.

---

## 8. Mutabakat servisi — eksik parçayı tamamlıyoruz

Önceki adımda şu tespit edilmişti: `VerifyAsync` ağ hatası fırlattığında ödeme `Pending` kalıyor, ama **iyzico callback'i tarayıcı redirect'i olduğu için otomatik tekrar denenmez.** Stripe webhook'undan farkı bu.

Bu servis olmadan: para çekilmiş, bilet verilmemiş, kimse fark etmemiş.

```csharp
namespace ReservationSystem.Infrastructure.BackgroundJobs;

public class PendingPaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingPaymentReconciliationService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ReconcilePendingPaymentsCommand, int>>();

                var count = await handler.HandleAsync(
                    new ReconcilePendingPaymentsCommand(MinimumAge, BatchSize: 50), ct);

                if (count > 0)
                    logger.LogInformation("{Count} bekleyen ödeme mutabakatı yapıldı.", count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mutabakat turu başarısız — sonraki turda tekrar denenecek.");
            }
        }
    }
}
```

**`MinimumAge` neden var:** Kullanıcı şu anda iyzico'nun ödeme formunu dolduruyor olabilir. 2 dakikadan yeni `Pending` kayıtlara dokunmak, normal akışı bozar.

Application tarafındaki handler, `CompletePaymentHandler` ile aynı mantığı kullanır — token ile `VerifyAsync`, sonuca göre confirm / fail / refund. Kod tekrarı olmasın diye ortak bir `PaymentSettlementService` çıkarılabilir; ilk sürümde iki handler'ın ayrı durması da kabul edilebilir.

**Kayıt:**

```csharp
// Infrastructure/DependencyInjection.cs
services.AddHostedService<ExpiredReservationCleanupService>();
services.AddHostedService<PendingPaymentReconciliationService>();
```

> **Ölçekleme notu:** Birden fazla API kopyası çalıştığında iki servis de her kopyada çalışır. `ExpiredReservationCleanupService` için sorun değil (optimistic locking çakışmaları yutuyor). Mutabakat için de idempotency koruyor. Ama gerçek üretimde dağıtık kilit (Redis) veya ayrı bir worker süreci tercih edilir. Faz 4 konusu.

---

## 9. Yapılandırma

`appsettings.json` — değerler boş, yapı belli:

```json
{
  "ConnectionStrings": { "Postgres": "", "Redis": "" },
  "Jwt": {
    "Issuer": "ReservationSystem",
    "Audience": "ReservationSystem.Client",
    "Key": "",
    "ExpiryMinutes": 60
  },
  "Iyzico": { "ApiKey": "", "SecretKey": "", "BaseUrl": "" },
  "Payment": { "CallbackUrl": "" },
  "Frontend": { "BaseUrl": "http://localhost:5173" },
  "Cors": { "Origins": [ "http://localhost:5173" ] },
  "Serilog": {
    "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.AspNetCore": "Warning" } }
  }
}
```

Gerçek değerler `user-secrets`'ta:

```bash
dotnet user-secrets set "Jwt:Key" "<en az 32 karakter rastgele dizge>"
dotnet user-secrets set "Payment:CallbackUrl" "https://<ngrok-adresin>/api/payments/callback"
```

> **JWT anahtarı** en az 256 bit (32 karakter) olmalı, yoksa `HmacSha256` çalışmaz. Üretmek için: `openssl rand -base64 48` veya PowerShell'de `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }))`.

---

## 10. ngrok — callback için zorunlu

iyzico `localhost` kabul etmez. Geliştirme sırasında:

```bash
ngrok http https://localhost:7001
```

Verdiği `https://abc123.ngrok-free.app` adresini `Payment:CallbackUrl` olarak ayarla.

**Dikkat:** Ücretsiz ngrok her yeniden başlatmada farklı adres verir — `user-secrets`'taki değeri güncellemeyi unutma. Ödeme testinde "callback hiç gelmiyor" sorununun en sık sebebi bu.

---

## 11. Docker Compose — API dahil

`docker-compose.yml` güncellemesi:

```yaml
services:
  postgres:
    # ... (mevcut tanım)

  redis:
    # ... (mevcut tanım)

  api:
    build:
      context: ./backend
      dockerfile: src/ReservationSystem.Api/Dockerfile
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Postgres: "Host=postgres;Port=5432;Database=reservationdb;Username=reservation;Password=localdev123"
      ConnectionStrings__Redis: "redis:6379"
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
```

`Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/ReservationSystem.Api/ReservationSystem.Api.csproj
RUN dotnet publish src/ReservationSystem.Api/ReservationSystem.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReservationSystem.Api.dll"]
```

**`depends_on` + `condition: service_healthy`** — healthcheck'leri bu yüzden yazmıştık. API, veritabanı hazır olmadan başlamaz.

**Ortam değişkenlerindeki çift alt çizgi** (`ConnectionStrings__Postgres`) .NET'in iç içe yapılandırma sözdizimi. Tek alt çizgi çalışmaz.

> **Gizli bilgiler compose dosyasında değil:** `localdev123` sadece yerel. iyzico anahtarları için `.env` dosyası + `env_file:` kullanılır, o dosya `.gitignore`'da.

---

## 12. Test edilecekler

Api katmanında birim testi yerine **entegrasyon testi** yazılır (`WebApplicationFactory` + Testcontainers):

- Kimliksiz istek `/api/reservations`'a 401 dönüyor
- Başkasının rezervasyonunu iptal etme denemesi 403 dönüyor (BR-15)
- Aynı koltuk için iki eşzamanlı istek: biri 201, diğeri 409 (BR-06)
- Callback endpoint'i JWT olmadan erişilebiliyor
- Rate limit aşımında 429 dönüyor
- Bilinmeyen istisna 500 dönüyor ve **mesaj sızdırmıyor**

Üçüncü madde özellikle değerli: projenin var oluş sebebi olan concurrency garantisini uçtan uca doğrulayan tek test.

---

## 13. Sonraki adım

1. Migration'ı veritabanına uygula, seed verisiyle doğrula
2. Postman/Swagger ile uçtan uca akışı manuel test et: kayıt → giriş → koltuk seç → ödeme başlat → callback
3. Frontend: koltuk haritası, geri sayım, ödeme formu, SignalR bağlantısı
4. Faz 4: k6 ile yük testi, CI/CD, outbox pattern

---

## 14. Uygulama notları (kod yazılırken çıkan sapmalar)

Doküman tasarım niyetini anlatıyor; kod yazılırken mevcut Application/Infrastructure imzalarına
uydurulması gereken yerler:

| Dokümandaki | Koddaki | Neden |
|---|---|---|
| `ICommandHandler<CancelReservationCommand, Unit>` | `…, bool` | Application'da `Unit` tipi yok; `CancelReservationCommand : ICommand<bool>` |
| `CurrentUser` Api'de | Api'ye **taşındı** | `Infrastructure/Identity/CurrentUser.cs` silindi, DI kaydı Api'ye alındı (§4'teki gerekçe) |
| `ReconcilePendingPaymentsCommand` handler'ı bağımsız | `CompletePaymentHandler`'ı çağırıyor | Aynı mantığın iki kopyası yerine tek kaynak; `CompletePaymentHandler` zaten idempotent (BR-12) |

**Henüz yok:** kayıt/giriş uçları. `Program.cs` JWT *doğrulaması* kuruyor ama token *üreten*
bir uç yok — `POST /api/auth/register` ve `POST /api/auth/login` ile bunların Application
tarafındaki handler'ları (parola hash'leme, token üretimi) ayrı bir adımın konusu. O gelene
kadar korumalı uçlar elle üretilmiş bir JWT ile test edilir.

**Henüz yok:** §12'deki entegrasyon testleri. `WebApplicationFactory` + Testcontainers
gerçek bir Docker daemon'ı gerektiriyor; yazılıp çalıştırılmadan bırakılmasınlar diye
ayrı adıma bırakıldı. Docker'sız yapılan uçtan uca duman testinin sonuçları:

| Kontrol | Sonuç |
|---|---|
| `GET /swagger/v1/swagger.json` | 200, 4 uç listeleniyor (callback `ApiExplorer`'dan gizli) |
| `POST /api/reservations` kimliksiz | 401 |
| `POST /api/payments/callback` kimliksiz, boş token | 302 → `…/odeme/sonuc?durum=gecersiz` |
| Bilinmeyen istisna (DB erişilemez) | 500 + `internal_error`, bağlantı dizesi **sızmıyor**, `traceId` var |
| `/api/reservations`'a 12 ardışık istek | `401 ×10`, `429 ×2` |
| `/api/payments/start`'a 7 ardışık istek | `401 ×5`, `429 ×2` |
