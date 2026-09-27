using Microsoft.EntityFrameworkCore;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSeat> EventSeats => Set<EventSeat>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Kimlikler Domain'de üretiliyor (Entity.Id = Guid.CreateVersion7()). EF Core
        // varsayılan olarak Guid anahtarı "eklenirken üretilir" sayar; bu durumda
        // takip edilen bir aggregate'e sonradan eklenen çocuk (ör. Reservation.StartPayment
        // ile gelen Payment) dolu Id'si yüzünden MEVCUT kayıt sanılır, INSERT yerine
        // UPDATE üretilir ve 0 satır etkilendiği için concurrency hatası alınır.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var id = entityType.FindProperty("Id");
            if (id?.ClrType == typeof(Guid) && id.IsPrimaryKey())
                id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Tek para birimi (TRY). Çoklu para birimine geçilirse Money value object gelir.
        builder.Properties<decimal>().HavePrecision(10, 2);
        base.ConfigureConventions(builder);
    }
}
