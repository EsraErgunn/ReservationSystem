# Application Katmanı

**Versiyon:** 0.1
**Dayandığı dokümanlar:** `gereksinim-dokumani.md`, `veritabani-semasi.md`, `domain-katmani.md`

---

## 1. Bu katmanın işi ne, ne değil

**İşi:** Bir kullanım senaryosunu baştan sona yürütmek — veriyi yükle, domain metodunu çağır, sonucu kaydet, dış servisi tetikle.

**İşi değil:** İş kuralı barındırmak. "Hold süresi 10 dakika" veya "en fazla 6 koltuk" kuralları Domain'de. Application bu kuralları *bilmez*, sadece domain metodunu çağırır ve fırlatan istisnayı yönetir.

Ayırt etme testi: Bir kuralı yazarken "bunu veritabanı olmadan test edebilir miyim?" diye sor. Evetse Domain'e ait.

---

## 2. MediatR kullanmıyoruz — gerekçe

MediatR, 2025'te ticari lisansa geçti. Belirli bir gelir eşiğinin altındaki kullanım için ücretsiz kalsa da, öğrenme projesinde lisans takibiyle uğraşmanın anlamı yok.

Daha önemlisi: MediatR'ın yaptığı şey aslında **20 satırlık bir arayüz + DI kaydı**. Onu elle yazmak, "MediatR sihir yapıyor" hissini ortadan kaldırır ve pipeline (logging, validation, transaction) kavramını gerçekten anlamanı sağlar.

```csharp
namespace ReservationSystem.Application.Common;

public interface ICommand<TResult>;

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQuery<TResult>;

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}
```

Controller doğrudan handler'ı enjekte eder:

```csharp
public class ReservationsController(
    ICommandHandler<CreateReservationCommand, ReservationDto> createHandler)
    : ControllerBase
{ ... }
```

> İleride pipeline davranışı (her komutu loglamak, doğrulamak) istersen, decorator pattern ile eklenir. `Scrutor` paketi bunu tek satırda yapar. O noktaya gelince değerlendiririz.

---

## 3. Klasör yapısı

```
ReservationSystem.Application/
├── Common/
│   ├── ICommand.cs
│   ├── IQuery.cs
│   └── ApplicationException.cs
├── Abstractions/
│   ├── IReservationRepository.cs
│   ├── IEventRepository.cs
│   ├── IEventSeatRepository.cs
│   ├── IUnitOfWork.cs
│   ├── IPaymentGateway.cs
│   ├── ISeatAvailabilityNotifier.cs
│   └── ICurrentUser.cs
├── Payments/
│   ├── Models/           ← sağlayıcıdan bağımsız ödeme modelleri
│   ├── StartPaymentCommand.cs
│   └── CompletePaymentCommand.cs
├── Reservations/
│   ├── CreateReservationCommand.cs
│   ├── CancelReservationCommand.cs
│   ├── ExpireReservationsCommand.cs
│   └── Dtos/
└── Events/
    └── GetSeatMapQuery.cs
```

---

## 4. Portlar (arayüzler)

Bunlar Application'ın dış dünyaya açılan delikleri. **Hiçbiri EF Core, Redis veya iyzico tipini içermez** — aksi halde bağımlılık yönü tersine döner.

### 4.1 Repository'ler

```csharp
namespace ReservationSystem.Application.Abstractions;

public interface IEventSeatRepository
{
    /// <summary>
    /// Belirtilen koltukları takip edilir (tracked) şekilde yükler.
    /// Concurrency token'ı EF Core yönetir; çağıran taraf bilmez.
    /// </summary>
    Task<IReadOnlyList<EventSeat>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<EventSeat>> GetByReservationAsync(
        Guid reservationId, CancellationToken ct);
}

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Ödeme akışı için: Items ve Payments dahil yükler.</summary>
    Task<Reservation?> GetWithPaymentsAsync(Guid id, CancellationToken ct);

    Task<Reservation?> GetByPaymentTokenAsync(string token, CancellationToken ct);

    /// <summary>BR-02: süresi dolmuş hold'lar.</summary>
    Task<IReadOnlyList<Reservation>> GetExpiredAsync(
        DateTime utcNow, int batchSize, CancellationToken ct);

    void Add(Reservation reservation);
}

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct);
}
```

### 4.2 `IUnitOfWork` — transaction sınırı

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);

    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
```

**Neden `ExecuteInTransactionAsync` var:** F maddesi gereği `Reservation`, `ReservationItem` ve `EventSeat` aynı transaction içinde güncellenmeli. `SaveChangesAsync` tek başına bunu garanti eder (EF Core tek SaveChanges'i transaction'a sarar), ancak ödeme akışında **birden fazla SaveChanges** gerekebilir — orada açık transaction şart.

### 4.3 `IPaymentGateway` — iyzico'nun izole edildiği yer

Bu arayüz, dokümanın en önemli parçalarından biri. `Iyzipay` isim alanından **tek bir tip bile** buraya sızmamalı.

```csharp
namespace ReservationSystem.Application.Abstractions;

public interface IPaymentGateway
{
    Task<PaymentInitResult> InitializeAsync(
        PaymentInitRequest request, CancellationToken ct);

    Task<PaymentVerificationResult> VerifyAsync(
        string providerToken, CancellationToken ct);

    /// <summary>E maddesi: hold süresi dolmuşken tamamlanan ödemenin iadesi.</summary>
    Task<RefundResult> RefundAsync(
        string providerPaymentId, decimal amount, CancellationToken ct);
}
```

```csharp
namespace ReservationSystem.Application.Payments.Models;

public record PaymentInitRequest(
    Guid ReservationId,
    decimal Amount,
    string BuyerName,
    string BuyerEmail,
    string CallbackUrl,
    IReadOnlyList<PaymentBasketItem> Items);

public record PaymentBasketItem(string Id, string Name, decimal Price);

public record PaymentInitResult(
    bool Success,
    string? ProviderToken,
    string? FormContent,
    string? ErrorMessage);

public record PaymentVerificationResult(
    PaymentOutcome Outcome,
    string? ProviderPaymentId,
    decimal PaidAmount,
    string? FailureReason);

public enum PaymentOutcome { Succeeded, Failed, Pending }

public record RefundResult(bool Success, string? ErrorMessage);
```

> **`PaymentBasketItem` neden var?** iyzico, sepet satırlarının gönderilmesini bekliyor (komisyon hesabı ve itiraz süreçleri için). Ama bu, Application'ın iyzico'yu bilmesi anlamına gelmiyor — bu model genel bir "ödeme kalemi" kavramı. Stripe'a geçsen de aynı model işe yarar.

### 4.4 Diğer portlar

```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAdmin { get; }
}

public interface ISeatAvailabilityNotifier
{
    /// <summary>FR-09: koltuk durumu değişince diğer kullanıcılara yayın.</summary>
    Task NotifySeatsChangedAsync(
        Guid eventId, IReadOnlyList<Guid> seatIds, CancellationToken ct);
}
```

**Zaman:** Ayrı bir `ITimeProvider` yazmıyoruz — .NET 8+ ile gelen `TimeProvider` soyut sınıfı bunu zaten karşılıyor ve test edilebilir (`FakeTimeProvider`, `Microsoft.Extensions.TimeProvider.Testing` paketinde).

---

## 5. Rezervasyon oluşturma — çekirdek senaryo

```csharp
namespace ReservationSystem.Application.Reservations;

public record CreateReservationCommand(
    Guid EventId,
    IReadOnlyList<Guid> EventSeatIds) : ICommand<ReservationDto>;

public class CreateReservationHandler(
    IEventRepository events,
    IEventSeatRepository seats,
    IReservationRepository reservations,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<CreateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> HandleAsync(
        CreateReservationCommand command, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAppException();

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var @event = await events.GetByIdAsync(command.EventId, ct)
            ?? throw new NotFoundAppException("event", command.EventId);

        var eventSeats = await seats.GetByIdsAsync(command.EventSeatIds, ct);

        if (eventSeats.Count != command.EventSeatIds.Count)
            throw new NotFoundAppException("event_seat", "Bazı koltuklar bulunamadı.");

        // Tüm kurallar burada değil — Domain'de. Application sadece çağırır.
        var reservation = Reservation.Create(userId, @event, eventSeats, utcNow);

        reservations.Add(reservation);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // BR-06: başka bir kullanıcı aynı koltuğu bizden önce aldı
            throw new ConflictAppException(
                "seat.taken",
                "Seçtiğiniz koltuklardan biri az önce başkası tarafından alındı.");
        }
        catch (DbUpdateException ex) when (IsActiveSeatUniqueViolation(ex))
        {
            // ux_reservation_items_active_seat ihlali — son savunma hattı devreye girdi
            throw new ConflictAppException(
                "seat.taken",
                "Seçtiğiniz koltuklardan biri az önce başkası tarafından alındı.");
        }

        await notifier.NotifySeatsChangedAsync(
            @event.Id, command.EventSeatIds, ct);

        return ReservationDto.From(reservation);
    }
}
```

### Dikkat edilecek noktalar

**A) `DbUpdateConcurrencyException` Application'da yakalanıyor**

Bu tip `Microsoft.EntityFrameworkCore` isim alanından geliyor — yani Application'ın EF Core'a bir miktar bağımlılığı var. Katı Clean Architecture'a göre bu bir sapma.

İki seçenek vardı:
1. Infrastructure'da yakalayıp kendi `ConcurrencyException`'ımıza çevirmek (repository'de `SaveChanges` sarmalanır)
2. Application'da doğrudan yakalamak

**Seçilen: 1. seçenek olmalı.** Yukarıdaki kod okunabilirlik için doğrudan yakalıyor, ama gerçek uygulamada `IUnitOfWork` implementasyonu bu istisnayı yakalayıp Application'ın tanıdığı bir tipe çevirir:

```csharp
// Infrastructure/UnitOfWork.cs
public async Task<int> SaveChangesAsync(CancellationToken ct)
{
    try
    {
        return await _context.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        throw new ConcurrencyConflictException(ex.Message, ex);
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")   // unique_violation
    {
        throw new UniqueConstraintException(ex.ConstraintName, ex);
    }
}
```

Böylece Application yalnızca kendi tiplerini bilir ve `Npgsql` isim alanı Infrastructure'da kalır. Yukarıdaki handler örneği buna göre sadeleşir.

**B) Retry yapmıyoruz — bilinçli**

Concurrency çakışmasında otomatik retry cazip görünüyor ama yanlış: koltuk gerçekten alındıysa tekrar denemek aynı sonucu verir. Kullanıcıya "bu koltuk gitti, başka seç" demek doğru davranış (BR-06: *sessiz başarısızlık olmamalı*).

Retry, ancak "aynı satırı iki farklı alan için güncelliyoruz" gibi gerçek yarış durumlarında anlamlı. Burada öyle değil.

**C) Bildirim `SaveChanges`'ten SONRA**

`NotifySeatsChangedAsync` transaction dışında, kayıt başarılı olduktan sonra çağrılıyor. Transaction içinde çağırsaydık ve transaction geri alınsaydı, kullanıcılara gerçekleşmemiş bir değişiklik yayınlamış olurduk.

> **Bilinen eksik:** Kayıt başarılı olup bildirim gönderilemezse (SignalR hatası), tutarsızlık oluşur. Gerçek çözümü outbox pattern. Bu projede kabul edilen bir eksiklik — Faz 3'te ele alınabilir.

---

## 6. Ödeme başlatma

```csharp
public record StartPaymentCommand(Guid ReservationId) : ICommand<StartPaymentResult>;

public record StartPaymentResult(string FormContent);

public class StartPaymentHandler(
    IReservationRepository reservations,
    IPaymentGateway gateway,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<PaymentOptions> options)
    : ICommandHandler<StartPaymentCommand, StartPaymentResult>
{
    public async Task<StartPaymentResult> HandleAsync(
        StartPaymentCommand command, CancellationToken ct)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetWithPaymentsAsync(command.ReservationId, ct)
            ?? throw new NotFoundAppException("reservation", command.ReservationId);

        // BR-13: sadece kendi rezervasyonun
        if (reservation.UserId != currentUser.UserId)
            throw new ForbiddenAppException();

        // C maddesi: varsa eski Pending ödeme Abandoned yapılır — Domain halleder
        var payment = reservation.StartPayment(utcNow);

        // BR-09: tutar rezervasyondan, istemciden DEĞİL
        var request = new PaymentInitRequest(
            ReservationId: reservation.Id,
            Amount: reservation.TotalAmount,
            BuyerName: /* kullanıcıdan */ "",
            BuyerEmail: /* kullanıcıdan */ "",
            CallbackUrl: options.Value.CallbackUrl,
            Items: reservation.Items
                .Select(i => new PaymentBasketItem(
                    i.EventSeatId.ToString(), "Bilet", i.PriceAtReservation))
                .ToList());

        var result = await gateway.InitializeAsync(request, ct);

        if (!result.Success || result.ProviderToken is null)
        {
            payment.MarkFailed(result.ErrorMessage ?? "Ödeme başlatılamadı.", utcNow);
            await uow.SaveChangesAsync(ct);
            throw new PaymentAppException(result.ErrorMessage ?? "Ödeme başlatılamadı.");
        }

        payment.AttachProviderToken(result.ProviderToken);
        await uow.SaveChangesAsync(ct);

        return new StartPaymentResult(result.FormContent!);
    }
}
```

**Sıralama kritik:** `AttachProviderToken` + `SaveChanges`, sağlayıcı çağrısından **sonra** yapılıyor. Token'ı almadan kaydedemeyiz; ama kaydetmeden callback gelirse token'ı tanıyamayız. Bu dar pencere kabul edilen bir risk — pratikte kullanıcının formu doldurması saniyeler sürer, kayıt milisaniyeler.

---

## 7. Ödeme tamamlama — en kritik handler

BR-10, BR-12, D ve E maddelerinin tamamı burada.

```csharp
public record CompletePaymentCommand(string ProviderToken) : ICommand<PaymentCompletionResult>;

public record PaymentCompletionResult(Guid ReservationId, bool Confirmed, string? Message);

public class CompletePaymentHandler(
    IReservationRepository reservations,
    IEventSeatRepository seats,
    IPaymentGateway gateway,
    IUnitOfWork uow,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<CompletePaymentCommand, PaymentCompletionResult>
{
    public async Task<PaymentCompletionResult> HandleAsync(
        CompletePaymentCommand command, CancellationToken ct)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetByPaymentTokenAsync(command.ProviderToken, ct)
            ?? throw new NotFoundAppException("payment", command.ProviderToken);

        var payment = reservation.Payments
            .Single(p => p.ProviderToken == command.ProviderToken);

        // BR-12: aynı callback iki kez gelirse ikinci kez işleme
        if (payment.Status != PaymentStatus.Pending)
            return new PaymentCompletionResult(
                reservation.Id,
                reservation.Status == ReservationStatus.Confirmed,
                "Bu ödeme zaten işlendi.");

        // BR-10: sonucu sağlayıcıya SORARAK öğren, istemciye güvenme
        var verification = await gateway.VerifyAsync(command.ProviderToken, ct);

        if (verification.Outcome != PaymentOutcome.Succeeded)
        {
            return await HandleFailureAsync(reservation, payment, verification, utcNow, ct);
        }

        // E maddesi: ödeme başarılı AMA hold süresi dolmuş
        if (utcNow > reservation.HeldUntil)
        {
            return await RefundExpiredAsync(reservation, payment, verification, utcNow, ct);
        }

        return await ConfirmAsync(reservation, payment, verification, utcNow, ct);
    }

    private async Task<PaymentCompletionResult> ConfirmAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            // D maddesi: tutar doğrulaması Domain'de yapılıyor
            payment.MarkSucceeded(
                verification.ProviderPaymentId!, verification.PaidAmount, utcNow);

            // F maddesi: Reservation + Items + EventSeats tek transaction
            reservation.Confirm(eventSeats, utcNow);

            await uow.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        await notifier.NotifySeatsChangedAsync(
            reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

        return new PaymentCompletionResult(reservation.Id, true, null);
    }

    private async Task<PaymentCompletionResult> HandleFailureAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

        await uow.ExecuteInTransactionAsync(async innerCt =>
        {
            payment.MarkFailed(verification.FailureReason ?? "Ödeme reddedildi.", utcNow);

            // BR-11: hold serbest bırakılır
            reservation.MarkFailed(eventSeats, utcNow);

            await uow.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        await notifier.NotifySeatsChangedAsync(
            reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

        return new PaymentCompletionResult(
            reservation.Id, false, verification.FailureReason);
    }

    private async Task<PaymentCompletionResult> RefundExpiredAsync(
        Reservation reservation, Payment payment,
        PaymentVerificationResult verification, DateTime utcNow, CancellationToken ct)
    {
        // Para çekildi ama koltuk artık bizim değil — iade şart
        var refund = await gateway.RefundAsync(
            verification.ProviderPaymentId!, verification.PaidAmount, ct);

        payment.MarkFailed(
            refund.Success
                ? "Rezervasyon süresi doldu, ödeme iade edildi."
                : $"Rezervasyon süresi doldu, iade BAŞARISIZ: {refund.ErrorMessage}",
            utcNow);

        await uow.SaveChangesAsync(ct);

        if (!refund.Success)
        {
            // Manuel müdahale gerekiyor — yüksek öncelikli log
            // (Infrastructure'daki logger ILogger ile, burada exception fırlatmıyoruz
            //  çünkü kullanıcıya durumu bildirmek gerekiyor)
        }

        return new PaymentCompletionResult(
            reservation.Id, false,
            "Rezervasyon süresi dolduğu için işlem tamamlanamadı. Ödemeniz iade edilecektir.");
    }
}
```

### Buradaki en önemli üç şey

1. **`VerifyAsync` çağrılmadan hiçbir durum değişmiyor.** Callback'e gelen istek tek başına hiçbir şey ifade etmiyor (BR-10).
2. **Idempotency kontrolü en başta.** `payment.Status != Pending` ise hiçbir yan etki üretmeden dönülüyor (BR-12).
3. **Süresi dolmuş ödeme iade ediliyor.** Bu senaryo nadirdir ama gerçekleşir; ele alınmazsa kullanıcının parası alınır, bileti verilmez.

> **İadenin başarısız olduğu durum** sistemin en kırılgan noktası. Bu projede yüksek öncelikli log ile yetiniliyor; gerçek bir üründe bu kayıt bir "manuel inceleme" kuyruğuna düşerdi.

---

## 8. Süresi dolan rezervasyonların temizlenmesi

BR-02. Arka plan servisi (Infrastructure'da `BackgroundService`) bu komutu periyodik çağırır.

```csharp
public record ExpireReservationsCommand(int BatchSize = 100) : ICommand<int>;

public class ExpireReservationsHandler(
    IReservationRepository reservations,
    IEventSeatRepository seats,
    IUnitOfWork uow,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<ExpireReservationsCommand, int>
{
    public async Task<int> HandleAsync(ExpireReservationsCommand command, CancellationToken ct)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var expired = await reservations.GetExpiredAsync(utcNow, command.BatchSize, ct);
        if (expired.Count == 0) return 0;

        var processed = 0;

        foreach (var reservation in expired)
        {
            var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

            try
            {
                await uow.ExecuteInTransactionAsync(async innerCt =>
                {
                    reservation.Expire(eventSeats, utcNow);
                    await uow.SaveChangesAsync(innerCt);
                    return true;
                }, ct);

                await notifier.NotifySeatsChangedAsync(
                    reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

                processed++;
            }
            catch (ConcurrencyConflictException)
            {
                // Kullanıcı tam o anda ödemeyi tamamladı ve rezervasyon Confirmed oldu.
                // Bu bir hata değil — atla, sonraki turda zaten listede olmayacak.
            }
        }

        return processed;
    }
}
```

**Neden tek tek, toplu değil:** `ExecuteUpdate` ile toplu güncelleme çok daha hızlı olurdu ama domain kurallarını atlar ve `xmin` concurrency kontrolünü devre dışı bırakır. Batch boyutu küçük tutuluyor (100), sıklık yüksek (5 saniyede bir) — bu, doğruluğu hıza tercih eden bilinçli bir takas.

**`ConcurrencyConflictException` yakalanıp yutuluyor:** Burası nadir ama gerçek bir yarış — temizlik görevi rezervasyonu okuduktan sonra, yazmadan önce kullanıcı ödemeyi tamamlarsa. Doğru davranış: kullanıcı kazanır, temizlik görevi geri çekilir.

---

## 9. Koltuk haritası sorgusu

Komuttan farklı olarak sorgular domain'den geçmez — doğrudan okuma modeli döner (CQRS'in okuma tarafı).

```csharp
public record GetSeatMapQuery(Guid EventId) : IQuery<SeatMapDto>;

public record SeatMapDto(
    Guid EventId,
    string EventTitle,
    IReadOnlyList<SeatMapItemDto> Seats);

public record SeatMapItemDto(
    Guid EventSeatId,
    string RowLabel,
    int SeatNumber,
    decimal Price,
    string Status);
```

Implementasyonu Infrastructure'da, `AsNoTracking()` ile projeksiyon yaparak. Domain entity'leri materyalize edilmez — NFR-01'deki p95 hedefi için gereksiz yük.

---

## 10. Hata tipleri

```csharp
namespace ReservationSystem.Application.Common;

public abstract class AppException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public class NotFoundAppException(string resource, object key)
    : AppException("not_found", $"{resource} bulunamadı: {key}");

public class ConflictAppException(string code, string message)
    : AppException(code, message);

public class ForbiddenAppException()
    : AppException("forbidden", "Bu işlem için yetkiniz yok.");

public class UnauthorizedAppException()
    : AppException("unauthorized", "Giriş yapmanız gerekiyor.");

public class PaymentAppException(string message)
    : AppException("payment_error", message);

public class ConcurrencyConflictException(string message, Exception? inner = null)
    : AppException("concurrency_conflict", message);
```

Api katmanı bunları HTTP durumlarına eşler:

| İstisna | HTTP |
|---|---|
| `NotFoundAppException` | 404 |
| `ConflictAppException`, `ConcurrencyConflictException` | 409 |
| `ForbiddenAppException` | 403 |
| `UnauthorizedAppException` | 401 |
| `DomainException` | 400 |
| `PaymentAppException` | 422 |

---

## 11. DI kaydı

```csharp
// ReservationSystem.Application/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<CreateReservationCommand, ReservationDto>,
            CreateReservationHandler>();

        services.AddScoped<
            ICommandHandler<StartPaymentCommand, StartPaymentResult>,
            StartPaymentHandler>();

        services.AddScoped<
            ICommandHandler<CompletePaymentCommand, PaymentCompletionResult>,
            CompletePaymentHandler>();

        services.AddScoped<
            ICommandHandler<ExpireReservationsCommand, int>,
            ExpireReservationsHandler>();

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
```

Handler sayısı arttıkça bu liste uzar. `Scrutor` ile assembly taraması yapılabilir:

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<CreateReservationCommand>()
    .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)))
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

> İlk 4-5 handler'ı elle yazmanı öneririm — DI'ın ne yaptığını görmek için. Liste rahatsız edici uzunluğa gelince Scrutor'a geç.

---

## 12. Bu katmanda test edilecekler

Domain testlerinden farklı olarak burada **mock** kullanılır:

```csharp
[Fact]
public async Task CompletePayment_WhenAlreadyProcessed_DoesNotCallGateway()
{
    var gateway = Substitute.For<IPaymentGateway>();
    // ... reservation'ı Succeeded payment ile hazırla

    var result = await handler.HandleAsync(new CompletePaymentCommand("tok_123"), default);

    await gateway.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

**Öncelikli test senaryoları:**
- Aynı token ile ikinci çağrı sağlayıcıya hiç gitmiyor (BR-12)
- Hold süresi dolmuşken başarılı ödeme → `RefundAsync` çağrılıyor (E maddesi)
- Ödeme reddedildiğinde koltuklar `Available` oluyor (BR-11)
- Başkasının rezervasyonu için ödeme başlatılamıyor (BR-13)
- `StartPayment` ikinci kez çağrıldığında eski ödeme `Abandoned` (C maddesi)

---

## 13. Sonraki adım

Infrastructure katmanı:
1. `AppDbContext` + EF Core konfigürasyonları (`veritabani-semasi.md`, Bölüm 3)
2. Repository ve `UnitOfWork` implementasyonları — istisna çevirimi dahil
3. `IyzicoPaymentGateway` — `Iyzipay` paketinin izole edildiği tek sınıf
4. `SignalRSeatNotifier`
5. `ExpiredReservationCleanupService` (`BackgroundService`)
6. İlk migration ve seed verisi
