using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// Sistemin çekirdek tablosu — concurrency kontrolü burada yapılır.
/// Version kolonu YOK: token olarak PostgreSQL'in xmin sistem kolonu kullanılıyor.
/// </summary>
public class EventSeatConfiguration : IEntityTypeConfiguration<EventSeat>
{
    public void Configure(EntityTypeBuilder<EventSeat> builder)
    {
        builder.ToTable("event_seats", t =>
        {
            t.HasCheckConstraint("ck_event_seats_price", "price >= 0");
            t.HasCheckConstraint("ck_event_seats_status",
                "status IN ('Available', 'Held', 'Sold')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Price).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // BR-06/BR-07: xmin okuma anındaki değeriyle WHERE'e eklenir; 0 satır
        // etkilenirse DbUpdateConcurrencyException. "İlk tamamlayan kazanır."
        builder.UseXminConcurrencyToken();

        builder.HasOne(x => x.Event)
            .WithMany(e => e.EventSeats)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // Etkinlikte kullanılmış koltuk mekândan silinemez — yetim bilet kaydı olmasın
        builder.HasOne(x => x.Seat)
            .WithMany()
            .HasForeignKey(x => x.SeatId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aynı koltuk bir etkinlikte iki kez satılamaz
        builder.HasIndex(x => new { x.EventId, x.SeatId })
            .IsUnique()
            .HasDatabaseName("ux_event_seats_event_seat");

        // NFR-01: koltuk haritası sorgusunun dayanağı
        builder.HasIndex(x => new { x.EventId, x.Status })
            .HasDatabaseName("ix_event_seats_availability");

        builder.Metadata
            .FindNavigation(nameof(EventSeat.Event))!
            .SetIsEagerLoaded(false);
    }
}
