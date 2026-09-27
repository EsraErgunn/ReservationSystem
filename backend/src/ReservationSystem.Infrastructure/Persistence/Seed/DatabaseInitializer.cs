using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Seed;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Açılışta bekleyen migration'ları uygular. Geliştirme ve Docker Compose için;
    /// çok instance'lı üretimde migration ayrı bir adımda (CI) çalıştırılmalı.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}

public class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string AdminEmail { get; set; } = "admin@etkinlik.local";
    public string AdminPassword { get; set; } = "Admin12345";
    public string DemoUserEmail { get; set; } = "demo@etkinlik.local";
    public string DemoUserPassword { get; set; } = "Demo12345";
}

/// <summary>
/// Migration + örnek veri. İdempotent: kullanıcılar e-postaya göre, katalog ise
/// "hiç mekân yoksa" koşuluyla eklenir — her açılışta güvenle çalışır.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (dbOptions.MigrateOnStartup)
        {
            logger.LogInformation("Bekleyen migration'lar uygulanıyor...");
            await db.Database.MigrateAsync(ct);
        }

        if (!seed.Enabled) return;

        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var utcNow = clock.GetUtcNow().UtcDateTime;

        await EnsureUserAsync(db, hasher, seed.AdminEmail, seed.AdminPassword, "Sistem Yöneticisi", admin: true, utcNow, ct);
        await EnsureUserAsync(db, hasher, seed.DemoUserEmail, seed.DemoUserPassword, "Demo Kullanıcı", admin: false, utcNow, ct);

        if (!await db.Venues.AnyAsync(ct))
        {
            SeedCatalog(db, utcNow);
            logger.LogInformation("Örnek mekân ve etkinlikler eklendi.");
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureUserAsync(
        AppDbContext db, IPasswordHasher hasher, string email, string password,
        string fullName, bool admin, DateTime utcNow, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct)) return;

        var user = new User(normalized, hasher.Hash(password), fullName, utcNow);
        if (admin) user.PromoteToAdmin();

        db.Users.Add(user);
    }

    private static void SeedCatalog(AppDbContext db, DateTime utcNow)
    {
        // Saatler UTC; 17:00 UTC = 20:00 TSİ
        var today = utcNow.Date;

        var harbiye = new Venue("Harbiye Açıkhava Tiyatrosu", "Harbiye Mah. Taşkışla Cad.", "İstanbul");
        var harbiyeSeats = harbiye.CreateLayout(
            [.. "ABCDEFGH".Select(c => (c.ToString(), 16))]);

        var sahne = new Venue("Kültür Merkezi Küçük Sahne", "Kızılay Meydanı No:1", "Ankara");
        var sahneSeats = sahne.CreateLayout(
            [.. "ABCDE".Select(c => (c.ToString(), 10))]);

        db.Venues.AddRange(harbiye, sahne);
        db.Seats.AddRange(harbiyeSeats);
        db.Seats.AddRange(sahneSeats);

        var senfoni = new Event(
            harbiye.Id, "Senfoni Gecesi: Klasikler",
            eventDate: today.AddDays(30).AddHours(17),
            salesStartAt: today.AddDays(-2),
            salesEndAt: today.AddDays(30).AddHours(15),
            description: "Filarmoni orkestrasından Beethoven, Dvořák ve Çaykovski yorumları.");
        senfoni.CreateSeats(harbiyeSeats, s => s.RowLabel switch
        {
            "A" or "B" => 900m,
            "C" or "D" or "E" => 650m,
            _ => 400m
        });

        var standup = new Event(
            sahne.Id, "Stand-up: Yılın Son Gösterisi",
            eventDate: today.AddDays(14).AddHours(17),
            salesStartAt: today.AddDays(-5),
            salesEndAt: today.AddDays(14).AddHours(16),
            description: "Bir buçuk saatlik yepyeni gösteri. +16 yaş sınırı vardır.");
        standup.CreateSeats(sahneSeats, s => s.RowLabel is "A" or "B" ? 500m : 350m);

        // BR-17 gösterimi: satışı henüz açılmamış etkinlik
        var caz = new Event(
            harbiye.Id, "Caz Festivali Açılış Konseri",
            eventDate: today.AddDays(60).AddHours(17),
            salesStartAt: today.AddDays(7),
            salesEndAt: today.AddDays(60).AddHours(15),
            description: "Festivalin açılış gecesi. Biletler satışa açıldığında koltuk seçimi aktifleşir.");
        caz.CreateSeats(harbiyeSeats, s => s.RowLabel is "A" or "B" or "C" ? 750m : 500m);

        db.Events.AddRange(senfoni, standup, caz);
    }
}
