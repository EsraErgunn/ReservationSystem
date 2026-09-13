| Primary key | `uuid` (v7 tercih edilir) | Dağıtık üretim, ID tahmin edilerek başkasının kaydına erişim riski yok |
  | Zaman damgası | `timestamptz` (UTC) | Saat dilimi karmaşası yaşamamak için tüm zamanlar UTC |
| Para | `numeric(10,2)` | `float`/`double` asla — kayan nokta yuvarlama hatası kabul edilemez |
| Enum | `text` + `CHECK` constraint | Veritabanında okunabilir; EF Core tarafında C# enum'a dönüştürülür |
| Silme | Soft delete yok (ilk sürümde) | Rezervasyonlar zaten `Cancelled` durumuyla korunuyor |

> **Para birimi notu:** `numeric(10,2)` maksimum 99.999.999,99 TRY'ye kadar destekler. Bilet fiyatları için fazlasıyla yeterli.

---

## 2. Tablolar

### 2.1 `users`

```sql
CREATE TABLE users (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email         text NOT NULL,
    password_hash text NOT NULL,
    full_name     text NOT NULL,
    role          text NOT NULL DEFAULT 'User',
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_users_role CHECK (role IN ('User', 'Admin'))
);

CREATE UNIQUE INDEX ux_users_email ON users (lower(email));
```

**Dikkat:** E-posta unique index'i `lower(email)` üzerinde. Aksi hâlde `Esra@x.com` ve `esra@x.com` iki ayrı hesap olarak kaydedilir.

---

### 2.2 `venues`

```sql
CREATE TABLE venues (
    id      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name    text NOT NULL,
    address text NOT NULL,
    city    text NOT NULL
);
```

---

### 2.3 `seats`

Mekâna ait fiziksel koltuk. Etkinlikten bağımsız, sabit veri.

```sql
CREATE TABLE seats (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    venue_id    uuid NOT NULL REFERENCES venues(id) ON DELETE CASCADE,
    row_label   text NOT NULL,
    seat_number integer NOT NULL,

    CONSTRAINT ck_seats_number CHECK (seat_number > 0)
);

CREATE UNIQUE INDEX ux_seats_venue_position
    ON seats (venue_id, row_label, seat_number);
```

**Dikkat:**
- `row` PostgreSQL'de ayrılmış kelimedir — kolon adı `row_label` seçildi.
- Unique index, aynı mekânda iki kez "A-12" koltuğu tanımlanmasını engeller.
- `ON DELETE CASCADE`: mekân silinirse koltukları da silinir (mekân henüz etkinlikte kullanılmamışsa anlamlı).

---

### 2.4 `events`

```sql
CREATE TABLE events (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    venue_id       uuid NOT NULL REFERENCES venues(id) ON DELETE RESTRICT,
    title          text NOT NULL,
    description    text,
    event_date     timestamptz NOT NULL,
    sales_start_at timestamptz NOT NULL,
    sales_end_at   timestamptz NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_events_sales_window CHECK (sales_start_at < sales_end_at),
    CONSTRAINT ck_events_sales_before_event CHECK (sales_end_at <= event_date)
);

CREATE INDEX ix_events_date ON events (event_date);
```

**Dikkat:** `ON DELETE RESTRICT` — etkinliği olan bir mekân silinemez. `CASCADE` kullanmak, satılmış biletleri sessizce yok etmek demek olurdu.

`CHECK` constraint'leri **BR-17**'yi (satış penceresi dışında rezervasyon yapılamaz) veritabanı seviyesinde destekler.

---

### 2.5 `event_seats`

Sistemin çekirdek tablosu. Concurrency kontrolü burada yapılır.

```sql
CREATE TABLE event_seats (
    id       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id uuid NOT NULL REFERENCES events(id) ON DELETE CASCADE,
    seat_id  uuid NOT NULL REFERENCES seats(id) ON DELETE RESTRICT,
    price    numeric(10,2) NOT NULL,
    status   text NOT NULL DEFAULT 'Available',

    CONSTRAINT ck_event_seats_price CHECK (price >= 0),
    CONSTRAINT ck_event_seats_status
        CHECK (status IN ('Available', 'Held', 'Sold'))
);

CREATE UNIQUE INDEX ux_event_seats_event_seat
    ON event_seats (event_id, seat_id);

CREATE INDEX ix_event_seats_availability
    ON event_seats (event_id, status);
```

**Dikkat:**
- `ux_event_seats_event_seat`: aynı koltuk bir etkinlikte iki kez satılamaz.
- `ix_event_seats_availability`: koltuk haritası sorgusu (`WHERE event_id = ? AND status = 'Available'`) için — **NFR-01**'deki p95 < 300 ms hedefinin temel dayanağı.
- **Version kolonu yok** — concurrency token olarak PostgreSQL'in `xmin` sistem kolonu kullanılıyor.

#### Optimistic locking: `xmin` (karar)

PostgreSQL'de her satırın `xmin` adında gizli bir sistem kolonu vardır ve satır her güncellendiğinde otomatik değişir. EF Core bunu doğrudan concurrency token olarak kullanabilir:

```csharp
modelBuilder.Entity<EventSeat>().UseXminAsConcurrencyToken();
```

İki seçenek değerlendirildi:

| Yaklaşım | Artı | Eksi |
|---|---|---|
| **`xmin`** (seçilen) | Ek kolon yok, artırma mekanizması yok; koruma hangi güncelleme yolundan gelinirse gelinsin geçerli | PostgreSQL'e bağımlılık |
| Açık `version integer` | Veritabanı bağımsız, taşınabilir | `ExecuteUpdate`, `ExecuteDelete` ve ham SQL, artırma mekanizmasını (interceptor) atlar — koruma sessizce devre dışı kalabilir |

**Karar gerekçesi:** Açık `version` kolonu bir `SaveChangesInterceptor` ile otomatikleştirilebilir, ancak `ExecuteUpdate`/`ExecuteDelete` bu interceptor'ı atladığı için koruma kod disiplinine bağımlı hâle gelir. `xmin` veritabanı seviyesinde çalıştığı için böyle bir açık bırakmaz.

Taşınabilirlik ihtiyacı ileride doğarsa: `version integer` kolonu eklenir, `UseXminAsConcurrencyToken()` yerine `IsConcurrencyToken()` kullanılır ve artırma bir interceptor'a devredilir. Küçük ve izole bir değişiklik.

> **Çalışma şekli:** EF Core, `xmin`'in okuma anındaki değerini `WHERE` şartına ekler:
> ```sql
> UPDATE event_seats SET status = 'Held' WHERE id = '...' AND xmin = 4823;
> ```
> Etkilenen satır sayısı 0 dönerse EF `DbUpdateConcurrencyException` fırlatır — BR-06'nın (ilk tamamlayan kazanır, diğeri açık hata alır) temel mekanizması budur.

> **`ON DELETE RESTRICT` on `seat_id`:** Bir etkinlikte kullanılmış koltuk, mekândan silinemez. Aksi hâlde satılmış bilet kaydı yetim kalır.

---

### 2.6 `reservations`

```sql
CREATE TABLE reservations (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    event_id   uuid NOT NULL REFERENCES events(id) ON DELETE RESTRICT,
    status     text NOT NULL DEFAULT 'Held',
    held_until timestamptz NOT NULL,
    total_amount numeric(10,2) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_reservations_status
        CHECK (status IN ('Held', 'Confirmed', 'Expired', 'Failed', 'Cancelled')),
    CONSTRAINT ck_reservations_total CHECK (total_amount >= 0)
);

CREATE INDEX ix_reservations_expiry
    ON reservations (held_until)
    WHERE status = 'Held';

CREATE INDEX ix_reservations_user ON reservations (user_id, created_at DESC);
```

**Dikkat:**
- `ix_reservations_expiry` **partial index**: yalnızca `Held` durumundaki satırları indeksler. Arka plan temizlik görevi (`BR-02`) saniyede bir bu sorguyu çalıştıracağı için, milyonlarca `Confirmed` satırı taramak yerine yalnızca aktif hold'lara bakar. Tablo büyüdükçe farkı dramatik olur.
- `event_id` neden burada? Rezervasyon satırlarından türetilebilir, ama "bu etkinliğin rezervasyonları" sorgusu için her seferinde join yapmak pahalı. Bilinçli, kontrollü bir denormalizasyon.
- `total_amount` da denormalize — satırlardan hesaplanabilir, ama ödeme tutarı doğrulaması (**D maddesi**) için tek bir güvenilir kaynak olması daha güvenli.

---

### 2.7 `reservation_items`

Çoka-çok ilişkinin açık (explicit) bağlantı tablosu.

```sql
CREATE TABLE reservation_items (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    reservation_id      uuid NOT NULL REFERENCES reservations(id) ON DELETE CASCADE,
    event_seat_id       uuid NOT NULL REFERENCES event_seats(id) ON DELETE RESTRICT,
    price_at_reservation numeric(10,2) NOT NULL,
    is_active           boolean NOT NULL DEFAULT true,

    CONSTRAINT ck_reservation_items_price CHECK (price_at_reservation >= 0)
);

-- KRİTİK: bir koltuk aynı anda yalnızca bir aktif rezervasyonda olabilir
CREATE UNIQUE INDEX ux_reservation_items_active_seat
    ON reservation_items (event_seat_id)
    WHERE is_active = true;

CREATE INDEX ix_reservation_items_reservation
    ON reservation_items (reservation_id);
```

**Bu şemanın en önemli satırı `ux_reservation_items_active_seat`.**

Önceki konuşmada değindiğimiz sorun buydu: uygulama kodundaki `if (seat.IsAvailable)` kontrolü tek başına yetmez, çünkü iki paralel istek aynı anda bu kontrolü geçebilir. Bu partial unique index **son savunma hattıdır** — veritabanı, ikinci INSERT'ü fiziksel olarak reddeder.

`is_active` alanının yaşam döngüsü:
- Rezervasyon oluşturulurken `true`
- Rezervasyon `Expired` / `Failed` / `Cancelled` olduğunda `false`
- Rezervasyon `Confirmed` olduğunda `true` kalır (koltuk kalıcı olarak dolu)

> **Neden `is_active` denormalize edildi?** PostgreSQL partial index'lerinde alt sorgu (subquery) kullanılamaz — `WHERE reservation_id IN (SELECT ...)` yazılamaz. Bu yüzden rezervasyon durumunun bir yansıması satır üzerinde tutulur. Bedeli: durum geçişlerinde iki tabloyu birlikte güncellemek gerekir, ki bu zaten **F maddesi** gereği aynı transaction içinde yapılacak.

---

### 2.8 `payments`

```sql
CREATE TABLE payments (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    reservation_id       uuid NOT NULL REFERENCES reservations(id) ON DELETE RESTRICT,
    provider_token       text,
    provider_payment_id  text,
    amount               numeric(10,2) NOT NULL,
    status               text NOT NULL DEFAULT 'Pending',
    failure_reason       text,
    created_at           timestamptz NOT NULL DEFAULT now(),
    completed_at         timestamptz,

    CONSTRAINT ck_payments_status
        CHECK (status IN ('Pending', 'Succeeded', 'Failed', 'Abandoned')),
    CONSTRAINT ck_payments_amount CHECK (amount > 0),
    CONSTRAINT ck_payments_completed
        CHECK ((status = 'Pending') = (completed_at IS NULL))
);

-- B maddesi: aynı iyzico token'ı iki kez işlenemez (idempotency)
CREATE UNIQUE INDEX ux_payments_provider_token
    ON payments (provider_token)
    WHERE provider_token IS NOT NULL;

-- C maddesi: bir rezervasyonun aynı anda yalnızca bir Pending ödemesi olabilir
CREATE UNIQUE INDEX ux_payments_single_pending
    ON payments (reservation_id)
    WHERE status = 'Pending';

CREATE INDEX ix_payments_reservation
    ON payments (reservation_id, created_at DESC);
```

**Dikkat:**
- `ux_payments_single_pending` — çift sekme senaryosunu (**C maddesi**) veritabanı seviyesinde engeller. Yeni ödeme başlatmadan önce mevcut `Pending` kayıt `Abandoned` yapılmalı, yoksa INSERT reddedilir. Bu, hatayı sessizce geçmek yerine kodu doğru yazmaya zorlar.
- `ck_payments_completed` — `Pending` ise `completed_at` boş, değilse dolu olmalı. Tutarsız kayıt yazılmasını engeller.
- `ON DELETE RESTRICT` — ödeme kaydı olan rezervasyon silinemez. Mali kayıtlar asla cascade ile silinmez.
- `Reservation` üzerinde `payment_status` kolonu **yok** — güncel durum `payments` tablosundan türetilir.

---

## 3. EF Core konfigürasyonu

Entity'ler temiz kalsın diye konfigürasyon `IEntityTypeConfiguration` sınıflarına ayrılır (Infrastructure katmanında).

### 3.1 `EventSeatConfiguration`

```csharp
public class EventSeatConfiguration : IEntityTypeConfiguration<EventSeat>
{
    public void Configure(EntityTypeBuilder<EventSeat> builder)
    {
        builder.ToTable("event_seats");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Price)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()   // enum -> text
            .HasMaxLength(20)
            .IsRequired();

        // Optimistic locking — PostgreSQL xmin sistem kolonu
        builder.UseXminAsConcurrencyToken();

        builder.HasIndex(x => new { x.EventId, x.SeatId }).IsUnique();
        builder.HasIndex(x => new { x.EventId, x.Status });

        builder.HasOne(x => x.Event)
            .WithMany(e => e.EventSeats)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Seat)
            .WithMany()
            .HasForeignKey(x => x.SeatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

### 3.2 `ReservationItemConfiguration`

```csharp
public class ReservationItemConfiguration : IEntityTypeConfiguration<ReservationItem>
{
    public void Configure(EntityTypeBuilder<ReservationItem> builder)
    {
        builder.ToTable("reservation_items");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PriceAtReservation)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        // Partial unique index — koltuk çakışmasına karşı son savunma
        builder.HasIndex(x => x.EventSeatId)
            .IsUnique()
            .HasFilter("is_active = true");

        builder.HasOne(x => x.Reservation)
            .WithMany(r => r.Items)
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.EventSeat)
            .WithMany()
            .HasForeignKey(x => x.EventSeatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

### 3.3 `PaymentConfiguration`

```csharp
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => x.ProviderToken)
            .IsUnique()
            .HasFilter("provider_token IS NOT NULL");

        builder.HasIndex(x => x.ReservationId)
            .IsUnique()
            .HasFilter("status = 'Pending'");

        builder.HasOne(x => x.Reservation)
            .WithMany(r => r.Payments)
            .HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

### 3.4 DbContext'te snake_case dönüşümü

Kolon adlarını elle yazmamak için:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

`EFCore.NamingConventions` paketiyle otomatik dönüşüm:

```csharp
options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
```

---

## 4. Şemanın karşıladığı iş kuralları

| Kural | Nerede uygulanıyor | Seviye |
|---|---|---|
| BR-02 (hold süre dolumu) | `ix_reservations_expiry` partial index | Performans desteği |
| BR-03 (en fazla 6 koltuk) | — | Uygulama katmanı |
| BR-04 (tek aktif hold) | `ux_reservation_items_active_seat` | **Veritabanı** |
| BR-06/07 (concurrency) | `xmin` + unique index | **Veritabanı** |
| BR-12 (ödeme idempotency) | `ux_payments_provider_token` | **Veritabanı** |
| BR-17 (satış penceresi) | `ck_events_sales_window` + uygulama kontrolü | Her ikisi |
| C maddesi (tek Pending ödeme) | `ux_payments_single_pending` | **Veritabanı** |

> **Prensip:** Veri bütünlüğünü koruyan kurallar veritabanına yazılır. Uygulama kodundaki kontroller kullanıcıya *anlamlı hata mesajı* vermek içindir; koruma için değil. İkisi birlikte çalışır.

---

## 5. Açık kalan konular

- **`EventSeat.Status` ile `ReservationItem.is_active` çift kaynak:** İkisi de "koltuk dolu mu" sorusunu yanıtlıyor. Her durum geçişinde ikisini de aynı transaction içinde güncellemek gerekiyor (**F maddesi**). Alternatif: `EventSeat.Status`'ü tamamen kaldırıp durumu her seferinde `reservation_items`'tan türetmek — okuma sorgularını yavaşlatır. İlk sürümde çift kaynak korunuyor, ölçüm sonrası tekrar değerlendirilecek.
- **Redis'in rolü:** Karar gereği veritabanı birincil kaynak. Redis yalnızca koltuk haritası cache'i ve rate limiting sayaçları için. Hold state'i Redis'te *tutulmuyor* — TTL tetikleyicisi yerine arka plan görevi kullanılacak.
- **Seed verisi:** Geliştirme için örnek mekân + koltuk düzeni üreten bir seeder gerekecek (1000 koltuklu salon, yük testi için).
  - **Migration stratejisi:** `dotnet ef migrations add` ile üretilecek; partial index'ler EF C
