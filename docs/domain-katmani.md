# Solution Yapısı ve Domain Katmanı

**Versiyon:** 0.1
**Dayandığı dokümanlar:** `gereksinim-dokumani.md` v0.1, `veritabani-semasi.md` v0.1

---

## 1. Solution kurulumu

```bash
mkdir ReservationSystem && cd ReservationSystem

dotnet new sln -n ReservationSystem

dotnet new classlib -n ReservationSystem.Domain
dotnet new classlib -n ReservationSystem.Application
dotnet new classlib -n ReservationSystem.Infrastructure
dotnet new webapi   -n ReservationSystem.Api

dotnet sln add **/*.csproj
```

### Katman referansları — yön kritik

```bash
# Application, Domain'i bilir
dotnet add ReservationSystem.Application reference ReservationSystem.Domain

# Infrastructure, Application'ı (ve dolaylı olarak Domain'i) bilir
dotnet add ReservationSystem.Infrastructure reference ReservationSystem.Application

# Api, her ikisini de bilir (DI kaydı için Infrastructure'a referans şart)
dotnet add ReservationSystem.Api reference ReservationSystem.Application
dotnet add ReservationSystem.Api reference ReservationSystem.Infrastructure
```

**Domain hiçbir projeye referans vermez.** Bu, Clean Architecture'ın tek cümlelik özeti.

### Domain katmanının bağımlılık kuralı

`ReservationSystem.Domain.csproj` içinde **hiçbir NuGet paketi olmamalı** — EF Core yok, Npgsql yok, MediatR yok. Sadece .NET'in kendisi.

Bunu test etmenin pratik yolu: Domain projesini açıp `using Microsoft.EntityFrameworkCore;` yazmayı dene. Derlenmemeli. Derleniyorsa bir yerde yanlış referans var.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <!-- PackageReference YOK -->
</Project>
```

### Klasör yapısı

```
ReservationSystem.Domain/
├── Common/
│   ├── Entity.cs
│   └── DomainException.cs
├── Enums/
│   ├── ReservationStatus.cs
│   ├── SeatStatus.cs
│   ├── PaymentStatus.cs
│   └── UserRole.cs
├── Entities/
│   ├── User.cs
│   ├── Venue.cs
│   ├── Seat.cs
│   ├── Event.cs
│   ├── EventSeat.cs
│   ├── Reservation.cs
│   ├── ReservationItem.cs
│   └── Payment.cs
└── Rules/
    └── ReservationRules.cs
```

---

## 2. Zaman yönetimi — önce bu karar

Domain katmanında **asla `DateTime.UtcNow` çağrılmaz.** Sebep: "hold süresi doldu mu" gibi kuralları test ederken 10 dakika beklemek zorunda kalırsın.

Çözüm: zaman, metoda parametre olarak geçirilir.

```csharp
// YANLIŞ — test edilemez
public void Confirm()
{
    if (DateTime.UtcNow > HeldUntil) throw new DomainException("Süre doldu");
}

// DOĞRU — test edilebilir
public void Confirm(DateTime utcNow)
{
    if (utcNow > HeldUntil) throw new DomainException("Süre doldu");
}
```

Uygulama katmanı `TimeProvider.System.GetUtcNow().UtcDateTime` ile gerçek zamanı geçirir; testler istedikleri zamanı geçirir. Domain saf kalır.

---

## 3. Ortak yapı taşları

### `Entity.cs`

```csharp
namespace ReservationSystem.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && GetType() == other.GetType() && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
```

> `Guid.CreateVersion7()` (.NET 9) zaman sıralı GUID üretir — rastgele GUID'in aksine veritabanı indeksini parçalamaz. .NET 8 kullanıyorsan `Guid.NewGuid()` yeterli, ama index fragmentasyonunu bilerek kabul etmiş olursun.

### `DomainException.cs`

```csharp
namespace ReservationSystem.Domain.Common;

public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
```

**`Code` neden var?** API katmanı bu kodu HTTP durum koduna ve kullanıcıya gösterilecek mesaja çevirir. Exception mesajını doğrudan kullanıcıya göstermek hem yerelleştirmeyi imkânsızlaştırır hem de iç detay sızdırır.

### Enum'lar

```csharp
namespace ReservationSystem.Domain.Enums;

public enum SeatStatus { Available, Held, Sold }

public enum ReservationStatus { Held, Confirmed, Expired, Failed, Cancelled }

public enum PaymentStatus { Pending, Succeeded, Failed, Abandoned }

public enum UserRole { User, Admin }
```

### `ReservationRules.cs`

Sihirli sayılar koda dağılmasın:

```csharp
namespace ReservationSystem.Domain.Rules;

public static class ReservationRules
{
    /// <summary>BR-01: Hold süresi.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    /// <summary>BR-03: Tek rezervasyonda maksimum koltuk.</summary>
    public const int MaxSeatsPerReservation = 6;
}
```

---

## 4. `EventSeat` — concurrency'nin merkezi

```csharp
using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Entities;

public class EventSeat : Entity
{
    public Guid EventId { get; private set; }
    public Guid SeatId { get; private set; }
    public decimal Price { get; private set; }
    public SeatStatus Status { get; private set; }

    public Event Event { get; private set; } = null!;
    public Seat Seat { get; private set; } = null!;

    private EventSeat() { }   // EF Core için

    public EventSeat(Guid eventId, Guid seatId, decimal price)
    {
        if (price < 0)
            throw new DomainException("seat.invalid_price", "Fiyat negatif olamaz.");

        EventId = eventId;
        SeatId = seatId;
        Price = price;
        Status = SeatStatus.Available;
    }

    public void Hold()
    {
        if (Status != SeatStatus.Available)
            throw new DomainException("seat.not_available", "Koltuk müsait değil.");

        Status = SeatStatus.Held;
    }

    public void Release()
    {
        if (Status == SeatStatus.Sold)
            throw new DomainException("seat.already_sold", "Satılmış koltuk serbest bırakılamaz.");

        Status = SeatStatus.Available;
    }

    public void MarkSold()
    {
        if (Status != SeatStatus.Held)
            throw new DomainException("seat.not_held", "Yalnızca hold edilmiş koltuk satılabilir.");

        Status = SeatStatus.Sold;
    }
}
```

**Dikkat edilecek noktalar:**

- Tüm setter'lar `private`. Dışarıdan `seat.Status = SeatStatus.Sold` yazılamaz — durum yalnızca metodlar üzerinden, kurallar kontrol edilerek değişir.
- `Release()` içindeki `Sold` kontrolü kritik: satılmış bir bileti yanlışlıkla serbest bırakmak, aynı koltuğun ikinci kez satılması demektir.
- `xmin` burada görünmüyor çünkü EF Core'un gölge (shadow) property'si olarak Infrastructure katmanında tanımlanacak. Domain'in PostgreSQL'den haberi olmamalı — kararın bedeli bu küçük soyutlama, ve doğru yerde duruyor.
- Parametresiz `private EventSeat()` constructor'ı EF Core'un materyalizasyon için ihtiyacı olan tek taviz.

---

## 5. `Reservation` — aggregate root

```csharp
using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;
using ReservationSystem.Domain.Rules;

namespace ReservationSystem.Domain.Entities;

public class Reservation : Entity
{
    private readonly List<ReservationItem> _items = [];
    private readonly List<Payment> _payments = [];

    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime HeldUntil { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<ReservationItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Reservation() { }

    public static Reservation Create(
        Guid userId,
        Event @event,
        IReadOnlyList<EventSeat> seats,
        DateTime utcNow)
    {
        if (seats.Count == 0)
            throw new DomainException("reservation.no_seats", "En az bir koltuk seçilmelidir.");

        // BR-03
        if (seats.Count > ReservationRules.MaxSeatsPerReservation)
            throw new DomainException(
                "reservation.too_many_seats",
                $"Tek rezervasyonda en fazla {ReservationRules.MaxSeatsPerReservation} koltuk seçilebilir.");

        // BR-17
        if (!@event.IsOnSale(utcNow))
            throw new DomainException("reservation.sales_closed", "Bilet satışı bu etkinlik için kapalı.");

        if (seats.Any(s => s.EventId != @event.Id))
            throw new DomainException("reservation.seat_event_mismatch", "Koltuklar bu etkinliğe ait değil.");

        if (seats.Select(s => s.Id).Distinct().Count() != seats.Count)
            throw new DomainException("reservation.duplicate_seat", "Aynı koltuk birden fazla kez seçilemez.");

        var reservation = new Reservation
        {
            UserId = userId,
            EventId = @event.Id,
            Status = ReservationStatus.Held,
            HeldUntil = utcNow.Add(ReservationRules.HoldDuration),   // BR-01
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        foreach (var seat in seats)
        {
            seat.Hold();
            reservation._items.Add(new ReservationItem(reservation.Id, seat.Id, seat.Price));
        }

        reservation.TotalAmount = reservation._items.Sum(i => i.PriceAtReservation);

        return reservation;
    }

    public bool IsExpired(DateTime utcNow) =>
        Status == ReservationStatus.Held && utcNow > HeldUntil;

    /// <summary>BR-10: Ödeme sunucu tarafında doğrulandıktan sonra çağrılır.</summary>
    public void Confirm(IReadOnlyList<EventSeat> seats, DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        // E maddesi: hold süresi dolarken ödeme yarışı
        if (utcNow > HeldUntil)
            throw new DomainException(
                "reservation.expired",
                "Rezervasyon süresi doldu; ödeme iade edilmelidir.");

        foreach (var seat in MatchSeats(seats))
            seat.MarkSold();

        Status = ReservationStatus.Confirmed;
        UpdatedAt = utcNow;
    }

    public void Expire(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Expired, seats, utcNow);

    public void Cancel(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Cancelled, seats, utcNow);

    public void MarkFailed(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Failed, seats, utcNow);

    private void Terminate(
        ReservationStatus newStatus,
        IReadOnlyList<EventSeat> seats,
        DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        foreach (var seat in MatchSeats(seats))
            seat.Release();

        foreach (var item in _items)
            item.Deactivate();

        Status = newStatus;
        UpdatedAt = utcNow;
    }

    public Payment StartPayment(DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        if (utcNow > HeldUntil)
            throw new DomainException("reservation.expired", "Rezervasyon süresi doldu.");

        // C maddesi: aynı anda tek Pending ödeme
        var pending = _payments.FirstOrDefault(p => p.Status == PaymentStatus.Pending);
        pending?.Abandon(utcNow);

        var payment = new Payment(Id, TotalAmount, utcNow);
        _payments.Add(payment);
        return payment;
    }

    private void EnsureStatus(ReservationStatus expected)
    {
        if (Status != expected)
            throw new DomainException(
                "reservation.invalid_state",
                $"Bu işlem için rezervasyon durumu '{expected}' olmalı, mevcut durum '{Status}'.");
    }

    /// <summary>Verilen koltuk listesinin bu rezervasyonun satırlarıyla tam eşleştiğini doğrular.</summary>
    private IEnumerable<EventSeat> MatchSeats(IReadOnlyList<EventSeat> seats)
    {
        var required = _items.Select(i => i.EventSeatId).ToHashSet();
        var provided = seats.Select(s => s.Id).ToHashSet();

        if (!required.SetEquals(provided))
            throw new DomainException(
                "reservation.seat_mismatch",
                "Verilen koltuklar rezervasyon satırlarıyla eşleşmiyor.");

        return seats;
    }
}
```

---

## 6. `ReservationItem` ve `Payment`

```csharp
public class ReservationItem : Entity
{
    public Guid ReservationId { get; private set; }
    public Guid EventSeatId { get; private set; }
    public decimal PriceAtReservation { get; private set; }
    public bool IsActive { get; private set; }

    private ReservationItem() { }

    internal ReservationItem(Guid reservationId, Guid eventSeatId, decimal price)
    {
        ReservationId = reservationId;
        EventSeatId = eventSeatId;
        PriceAtReservation = price;
        IsActive = true;
    }

    internal void Deactivate() => IsActive = false;
}
```

> **`internal` neden?** `ReservationItem` yalnızca `Reservation` tarafından oluşturulup değiştirilebilmeli. Uygulama katmanının doğrudan `new ReservationItem(...)` yazabilmesi, aggregate'in kurallarını atlaması demek olurdu.

```csharp
public class Payment : Entity
{
    public Guid ReservationId { get; private set; }
    public string? ProviderToken { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private Payment() { }

    internal Payment(Guid reservationId, decimal amount, DateTime utcNow)
    {
        if (amount <= 0)
            throw new DomainException("payment.invalid_amount", "Tutar sıfırdan büyük olmalı.");

        ReservationId = reservationId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = utcNow;
    }

    public void AttachProviderToken(string token)
    {
        EnsurePending();
        ProviderToken = token;
    }

    /// <summary>BR-09 / D maddesi: sağlayıcıdan dönen tutar doğrulanır.</summary>
    public void MarkSucceeded(string providerPaymentId, decimal paidAmount, DateTime utcNow)
    {
        EnsurePending();

        if (paidAmount != Amount)
            throw new DomainException(
                "payment.amount_mismatch",
                "Ödenen tutar beklenen tutarla eşleşmiyor.");

        ProviderPaymentId = providerPaymentId;
        Status = PaymentStatus.Succeeded;
        CompletedAt = utcNow;
    }

    public void MarkFailed(string reason, DateTime utcNow)
    {
        EnsurePending();
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        CompletedAt = utcNow;
    }

    internal void Abandon(DateTime utcNow)
    {
        EnsurePending();
        Status = PaymentStatus.Abandoned;
        CompletedAt = utcNow;
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.Pending)
            throw new DomainException(
                "payment.not_pending",
                "Tamamlanmış bir ödeme değiştirilemez.");
    }
}
```

---

## 7. Destekleyici entity'ler

```csharp
public class Event : Entity
{
    public Guid VenueId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTime EventDate { get; private set; }
    public DateTime SalesStartAt { get; private set; }
    public DateTime SalesEndAt { get; private set; }

    private readonly List<EventSeat> _eventSeats = [];
    public IReadOnlyCollection<EventSeat> EventSeats => _eventSeats.AsReadOnly();

    private Event() { }

    public Event(Guid venueId, string title, DateTime eventDate,
                 DateTime salesStartAt, DateTime salesEndAt)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("event.invalid_title", "Etkinlik başlığı boş olamaz.");

        if (salesStartAt >= salesEndAt)
            throw new DomainException("event.invalid_sales_window",
                "Satış başlangıcı bitişten önce olmalı.");

        if (salesEndAt > eventDate)
            throw new DomainException("event.sales_after_event",
                "Satış, etkinlik tarihinden sonra bitemez.");

        VenueId = venueId;
        Title = title;
        EventDate = eventDate;
        SalesStartAt = salesStartAt;
        SalesEndAt = salesEndAt;
    }

    /// <summary>BR-17</summary>
    public bool IsOnSale(DateTime utcNow) =>
        utcNow >= SalesStartAt && utcNow <= SalesEndAt;
}

public class Seat : Entity
{
    public Guid VenueId { get; private set; }
    public string RowLabel { get; private set; } = null!;
    public int SeatNumber { get; private set; }

    private Seat() { }

    public Seat(Guid venueId, string rowLabel, int seatNumber)
    {
        if (seatNumber <= 0)
            throw new DomainException("seat.invalid_number", "Koltuk numarası pozitif olmalı.");

        VenueId = venueId;
        RowLabel = rowLabel;
        SeatNumber = seatNumber;
    }

    public string Label => $"{RowLabel}-{SeatNumber}";
}

public class Venue : Entity
{
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;

    private Venue() { }

    public Venue(string name, string address, string city)
    {
        Name = name;
        Address = address;
        City = city;
    }
}

public class User : Entity
{
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private User() { }

    public User(string email, string passwordHash, string fullName, DateTime utcNow)
    {
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        FullName = fullName;
        Role = UserRole.User;
        CreatedAt = utcNow;
    }
}
```

---

## 8. Bilinçli sapmalar ve gerekçeleri

### 8.1 `Reservation` ve `EventSeat` ayrı aggregate olmalı mıydı?

Katı DDD yorumuna göre evet: iki aggregate aynı transaction içinde değiştirilmemeli, `EventSeat` durumu domain event ile eventual consistency üzerinden güncellenmeli.

Bu projede **kasıtlı olarak sapıldı**. Sebep: koltuk satışında eventual consistency, aynı koltuğun iki kez satılması riskini doğurur. Anlık tutarlılık bu domain'de pazarlık konusu değil. Bu yüzden `Reservation.Confirm()` metodu `EventSeat` nesnelerini parametre olarak alır ve ikisi tek transaction'da güncellenir — **F maddesinin** domain karşılığı.

Bedeli: `Confirm`, `Expire`, `Cancel` metodları koltuk listesi parametresi istiyor, bu da API'yi biraz hantallaştırıyor. `MatchSeats` doğrulaması bu hantallığın yanlış kullanılmasını engelliyor.

### 8.2 Exception mi, Result pattern mi?

Burada exception kullanıldı: domain kuralı ihlali gerçekten istisnai bir durum ve kod okunabilirliği yüksek kalıyor. Result pattern (`Result<T>` dönen metodlar) performans açısından daha iyi ama her çağrıyı `if (result.IsFailure)` ile sarmayı gerektirir.

İlerleyen fazlarda karşılaştırmak istersen: sıcak yol (koltuk müsaitlik kontrolü) Result'a çevrilip ölçülebilir.

### 8.3 `decimal` mı, `Money` value object mü?

Şimdilik `decimal`. Tek para birimi (TRY) olduğu sürece yeterli. Gerçek e-ticaret sitesine geçişte çoklu para birimi gerekirse `Money(decimal Amount, string Currency)` value object'i eklenecek — o zaman `TotalAmount` ve `Price` alanları buna dönüşür.

---

## 9. Domain'i test etme

Domain katmanı veritabanı olmadan test edilebildiği için (**NFR-12**) ilk testler burada yazılır:

```csharp
[Fact]
public void Create_WhenSalesClosed_Throws()
{
    var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    var @event = new Event(
        venueId: Guid.CreateVersion7(),
        title: "Konser",
        eventDate: now.AddDays(30),
        salesStartAt: now.AddDays(1),      // satış henüz başlamadı
        salesEndAt: now.AddDays(20));

    var seat = new EventSeat(@event.Id, Guid.CreateVersion7(), 250m);

    var ex = Assert.Throws<DomainException>(() =>
        Reservation.Create(Guid.CreateVersion7(), @event, [seat], now));

    Assert.Equal("reservation.sales_closed", ex.Code);
}

[Fact]
public void Confirm_AfterHoldExpired_Throws()
{
    var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    var (@event, seat) = BuildOnSaleEvent(now);

    var reservation = Reservation.Create(Guid.CreateVersion7(), @event, [seat], now);

    // 11 dakika sonra — hold süresi 10 dakika
    var later = now.AddMinutes(11);

    var ex = Assert.Throws<DomainException>(() =>
        reservation.Confirm([seat], later));

    Assert.Equal("reservation.expired", ex.Code);
}
```

İkinci test, zamanın parametre olarak geçirilmesinin neden önemli olduğunu gösteriyor: 11 dakika beklemeden hold süresi dolmuş senaryosu test edilebiliyor.

**Yazılması gereken diğer testler:**
- 7 koltuk seçilirse `too_many_seats` (BR-03)
- Aynı koltuk iki kez seçilirse `duplicate_seat`
- `Confirm` sonrası koltukların `Sold` olduğu
- `Expire` sonrası koltukların `Available`, satırların `IsActive = false` olduğu
- `StartPayment` ikinci kez çağrıldığında ilk ödemenin `Abandoned` olduğu (C maddesi)
- `MarkSucceeded` yanlış tutarla çağrılırsa `amount_mismatch` (D maddesi)

---

## 10. Sonraki adım

1. Bu entity'leri yaz ve yukarıdaki testleri geçir (veritabanı yok, paket yok — saf C#)
2. Application katmanı: `CreateReservationCommand`, repository arayüzleri (`IReservationRepository`, `IPaymentGateway`)
3. Infrastructure: `AppDbContext`, EF Core konfigürasyonları, ilk migration
4. Api: controller'lar ve `DomainException` → HTTP durum kodu eşleme
