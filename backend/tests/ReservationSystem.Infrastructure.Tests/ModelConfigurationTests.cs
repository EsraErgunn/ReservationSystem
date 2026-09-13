using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Infrastructure.Persistence;

namespace ReservationSystem.Infrastructure.Tests;

/// <summary>
/// Model kurulumunu veritabanı olmadan doğrular — EF Core modeli bellekte kurar,
/// bağlantı açmaz. `veritabani-semasi.md` Bölüm 2–3'teki şema kararlarının
/// koda geçtiğini test eder.
/// </summary>
public class ModelConfigurationTests : IDisposable
{
    private readonly AppDbContext _context = new AppDbContextFactory().CreateDbContext([]);

    /// <summary>
    /// CHECK constraint'ler çalışma zamanındaki read-optimized modelde tutulmaz —
    /// yalnızca tasarım zamanı modelinde bulunur. Tüm doğrulamalar bu model
    /// üzerinden yapılıyor; migration da aynı modelden üretiliyor.
    /// </summary>
    private readonly IModel _model;

    public ModelConfigurationTests() =>
        _model = _context.GetService<IDesignTimeModel>().Model;

    public void Dispose() { _context.Dispose(); GC.SuppressFinalize(this); }

    private IEntityType Entity<T>() => _model.FindEntityType(typeof(T))!;

    private IIndex Index<T>(string name) =>
        Entity<T>().GetIndexes().Single(i => i.GetDatabaseName() == name);

    [Fact]
    public void ModelBuildsWithoutErrors()
    {
        Assert.NotNull(_model);
        Assert.Equal(8, _model.GetEntityTypes().Count());
    }

    [Theory]
    [InlineData(typeof(User), "users")]
    [InlineData(typeof(Venue), "venues")]
    [InlineData(typeof(Seat), "seats")]
    [InlineData(typeof(Event), "events")]
    [InlineData(typeof(EventSeat), "event_seats")]
    [InlineData(typeof(Reservation), "reservations")]
    [InlineData(typeof(ReservationItem), "reservation_items")]
    [InlineData(typeof(Payment), "payments")]
    public void EntitiesMapToSnakeCaseTables(Type entity, string table)
    {
        Assert.Equal(table, _model.FindEntityType(entity)!.GetTableName());
    }

    // ---- §1: tip eşlemeleri ------------------------------------------------

    [Fact]
    public void MoneyUsesNumeric10Comma2()
    {
        foreach (var (entity, property) in new (IEntityType, string)[]
        {
            (Entity<EventSeat>(), nameof(EventSeat.Price)),
            (Entity<Reservation>(), nameof(Reservation.TotalAmount)),
            (Entity<ReservationItem>(), nameof(ReservationItem.PriceAtReservation)),
            (Entity<Payment>(), nameof(Payment.Amount))
        })
        {
            var money = entity.FindProperty(property)!;
            Assert.Equal(10, money.GetPrecision());
            Assert.Equal(2, money.GetScale());
        }
    }

    /// <summary>Tüm zamanlar timestamptz (UTC) — float para, naive timestamp yasak.</summary>
    [Fact]
    public void AllTimestampsUseTimestamptz()
    {
        var timestamps = _model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

        Assert.NotEmpty(timestamps);
        Assert.All(timestamps, p =>
            Assert.Equal("timestamp with time zone", p.GetColumnType()));
    }

    [Fact]
    public void EnumsArePersistedAsText()
    {
        foreach (var (entity, property) in new (IEntityType, string)[]
        {
            (Entity<EventSeat>(), nameof(EventSeat.Status)),
            (Entity<Reservation>(), nameof(Reservation.Status)),
            (Entity<Payment>(), nameof(Payment.Status)),
            (Entity<User>(), nameof(User.Role))
        })
        {
            Assert.Equal(typeof(string), entity.FindProperty(property)!.GetProviderClrType());
        }
    }

    // ---- §2: CHECK constraint'ler ------------------------------------------

    [Theory]
    [InlineData(typeof(User), "ck_users_role")]
    [InlineData(typeof(Seat), "ck_seats_number")]
    [InlineData(typeof(Event), "ck_events_sales_window")]
    [InlineData(typeof(Event), "ck_events_sales_before_event")]
    [InlineData(typeof(EventSeat), "ck_event_seats_price")]
    [InlineData(typeof(EventSeat), "ck_event_seats_status")]
    [InlineData(typeof(Reservation), "ck_reservations_status")]
    [InlineData(typeof(Reservation), "ck_reservations_total")]
    [InlineData(typeof(ReservationItem), "ck_reservation_items_price")]
    [InlineData(typeof(Payment), "ck_payments_status")]
    [InlineData(typeof(Payment), "ck_payments_amount")]
    [InlineData(typeof(Payment), "ck_payments_completed")]
    public void CheckConstraintExists(Type entity, string name)
    {
        var constraints = _model.FindEntityType(entity)!.GetCheckConstraints();
        Assert.Contains(constraints, c => c.Name == name);
    }

    /// <summary>Enum CHECK'leri C# enum üyeleriyle birebir örtüşmeli.</summary>
    [Theory]
    [InlineData(typeof(EventSeat), "ck_event_seats_status", typeof(Domain.Enums.SeatStatus))]
    [InlineData(typeof(Reservation), "ck_reservations_status", typeof(Domain.Enums.ReservationStatus))]
    [InlineData(typeof(Payment), "ck_payments_status", typeof(Domain.Enums.PaymentStatus))]
    [InlineData(typeof(User), "ck_users_role", typeof(Domain.Enums.UserRole))]
    public void EnumCheckConstraintCoversEveryEnumMember(Type entity, string name, Type enumType)
    {
        var sql = _model.FindEntityType(entity)!
            .GetCheckConstraints().Single(c => c.Name == name).Sql;

        Assert.All(Enum.GetNames(enumType), member =>
            Assert.Contains($"'{member}'", sql));

        // Fazladan değer de olmamalı
        Assert.Equal(
            Enum.GetNames(enumType).Length,
            sql.Count(ch => ch == '\'') / 2);
    }

    // ---- §2.5: concurrency -------------------------------------------------

    [Theory]
    [InlineData(typeof(EventSeat))]
    [InlineData(typeof(Reservation))]
    public void ConcurrencyTokenIsXminShadowProperty(Type entity)
    {
        var xmin = _model.FindEntityType(entity)!.FindProperty("xmin");

        Assert.NotNull(xmin);
        Assert.True(xmin.IsConcurrencyToken);
        Assert.True(xmin.IsShadowProperty());   // Domain entity'sinde karşılığı yok
        Assert.Equal(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);
    }

    /// <summary>Şema kararı: açık version kolonu YOK, xmin kullanılıyor.</summary>
    [Fact]
    public void NoExplicitVersionColumn()
    {
        var versionColumns = _model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.Name.Contains("version", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(versionColumns);
    }

    // ---- §2: index'ler -----------------------------------------------------

    [Fact]
    public void ActiveSeatUniqueIndexIsPartialOnIsActive()
    {
        var index = Index<ReservationItem>("ux_reservation_items_active_seat");

        Assert.True(index.IsUnique);
        Assert.Equal("is_active = true", index.GetFilter());
        Assert.Equal(nameof(ReservationItem.EventSeatId), Assert.Single(index.Properties).Name);
    }

    /// <summary>C maddesi: bir rezervasyonun aynı anda tek Pending ödemesi olabilir.</summary>
    [Fact]
    public void SinglePendingPaymentIndexIsPartialOnStatus()
    {
        var index = Index<Payment>("ux_payments_single_pending");

        Assert.True(index.IsUnique);
        Assert.Equal("status = 'Pending'", index.GetFilter());
        Assert.Equal(nameof(Payment.ReservationId), Assert.Single(index.Properties).Name);
    }

    /// <summary>BR-02: temizlik görevi yalnızca aktif hold'ları taramalı.</summary>
    [Fact]
    public void ExpiryIndexIsPartialOnHeldStatus()
    {
        var index = Index<Reservation>("ix_reservations_expiry");

        Assert.False(index.IsUnique);
        Assert.Equal("status = 'Held'", index.GetFilter());
        Assert.Equal(nameof(Reservation.HeldUntil), Assert.Single(index.Properties).Name);
    }

    [Theory]
    [InlineData("ux_payments_provider_token")]
    [InlineData("ux_payments_provider_payment_id")]
    public void PaymentTokenIndexesAreUniqueAndNullTolerant(string indexName)
    {
        var index = Index<Payment>(indexName);

        Assert.True(index.IsUnique);
        Assert.Contains("IS NOT NULL", index.GetFilter());
    }

    [Theory]
    [InlineData(typeof(EventSeat), "ux_event_seats_event_seat")]
    [InlineData(typeof(EventSeat), "ix_event_seats_availability")]
    [InlineData(typeof(Seat), "ux_seats_venue_position")]
    [InlineData(typeof(Event), "ix_events_date")]
    [InlineData(typeof(Reservation), "ix_reservations_user")]
    [InlineData(typeof(ReservationItem), "ix_reservation_items_reservation")]
    [InlineData(typeof(Payment), "ix_payments_reservation")]
    public void SchemaIndexExistsUnderDocumentedName(Type entity, string name)
    {
        Assert.Contains(
            _model.FindEntityType(entity)!.GetIndexes(),
            i => i.GetDatabaseName() == name);
    }

    /// <summary>
    /// Kısmi index filtreleri ham SQL — kolon adı yanlışsa migration üretilir
    /// ama veritabanı reddeder. Filtrede geçen adların gerçek kolon adları
    /// olduğunu doğruluyoruz.
    /// </summary>
    [Fact]
    public void PartialIndexFiltersReferenceRealColumnNames()
    {
        foreach (var entity in _model.GetEntityTypes())
        {
            var columns = entity.GetProperties().Select(p => p.GetColumnName()).ToHashSet();

            foreach (var index in entity.GetIndexes())
            {
                var filter = index.GetFilter();
                if (string.IsNullOrWhiteSpace(filter)) continue;

                var referenced = filter
                    .Split([' ', '(', ')', '='], StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Contains('_'));

                Assert.All(referenced, token =>
                    Assert.True(columns.Contains(token),
                        $"{entity.GetTableName()}.{index.GetDatabaseName()} filtresi "
                        + $"'{token}' kolonuna atıf yapıyor ama böyle bir kolon yok."));
            }
        }
    }

    /// <summary>
    /// ux_users_email şemada lower(email) üzerinde; EF Core ifade tabanlı index'i
    /// modelde tutamıyor, migration'da ham SQL ile geliyor. Modelde yanlışlıkla
    /// düz bir email index'i belirirse burası uyarır — o index sessizce
    /// büyük/küçük harf ikiliğine izin verirdi.
    /// </summary>
    [Fact]
    public void EmailIndexIsNotDeclaredInModel()
    {
        Assert.Empty(Entity<User>().GetIndexes());
    }

    // ---- Aggregate erişimi -------------------------------------------------

    [Theory]
    [InlineData(typeof(Reservation), nameof(Reservation.Items))]
    [InlineData(typeof(Reservation), nameof(Reservation.Payments))]
    [InlineData(typeof(Event), nameof(Event.EventSeats))]
    public void CollectionsUseBackingFields(Type entity, string navigation)
    {
        var nav = _model.FindEntityType(entity)!.FindNavigation(navigation)!;

        Assert.Equal(PropertyAccessMode.Field, nav.GetPropertyAccessMode());
        Assert.NotNull(nav.FieldInfo);
    }

    // ---- Silme davranışı ---------------------------------------------------

    /// <summary>Mali kayıtlar ve satılmış bilet izleri asla cascade ile silinmez.</summary>
    [Theory]
    [InlineData(typeof(Payment), nameof(Payment.ReservationId))]
    [InlineData(typeof(ReservationItem), nameof(ReservationItem.EventSeatId))]
    [InlineData(typeof(Reservation), nameof(Reservation.UserId))]
    [InlineData(typeof(Reservation), nameof(Reservation.EventId))]
    [InlineData(typeof(Event), nameof(Event.VenueId))]
    [InlineData(typeof(EventSeat), nameof(EventSeat.SeatId))]
    public void ForeignKeyIsRestricted(Type entity, string foreignKeyProperty)
    {
        var fk = _model.FindEntityType(entity)!.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == foreignKeyProperty));

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Theory]
    [InlineData(typeof(ReservationItem), nameof(ReservationItem.ReservationId))]
    [InlineData(typeof(EventSeat), nameof(EventSeat.EventId))]
    [InlineData(typeof(Seat), nameof(Seat.VenueId))]
    public void ForeignKeyCascades(Type entity, string foreignKeyProperty)
    {
        var fk = _model.FindEntityType(entity)!.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == foreignKeyProperty));

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    // ---- Diğer -------------------------------------------------------------

    [Fact]
    public void ComputedSeatLabelIsNotPersisted()
    {
        Assert.Null(Entity<Seat>().FindProperty(nameof(Seat.Label)));
    }

    /// <summary>Şemada var, Domain'de yok — shadow property olarak eklendi.</summary>
    [Fact]
    public void EventCreatedAtIsDatabaseGeneratedShadowProperty()
    {
        var createdAt = Entity<Event>().FindProperty("CreatedAt")!;

        Assert.True(createdAt.IsShadowProperty());
        Assert.Equal("created_at", createdAt.GetColumnName());
        Assert.Equal("now()", createdAt.GetDefaultValueSql());
    }
}
