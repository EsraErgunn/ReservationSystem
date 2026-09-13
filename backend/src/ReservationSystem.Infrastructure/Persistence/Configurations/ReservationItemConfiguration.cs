using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class ReservationItemConfiguration : IEntityTypeConfiguration<ReservationItem>
{
    public void Configure(EntityTypeBuilder<ReservationItem> builder)
    {
        builder.ToTable("reservation_items", t => t.HasCheckConstraint(
            "ck_reservation_items_price", "price_at_reservation >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PriceAtReservation).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        // Etkinlikte kullanılmış koltuk silinemez
        builder.HasOne<EventSeat>()
            .WithMany()
            .HasForeignKey(x => x.EventSeatId)
            .OnDelete(DeleteBehavior.Restrict);

        // Şemanın en önemli satırı. Uygulamadaki müsaitlik kontrolü tek başına
        // yetmez — iki paralel istek aynı anda o kontrolü geçebilir. Bu partial
        // unique index son savunma hattı: veritabanı ikinci INSERT'ü fiziksel
        // olarak reddeder. CreateReservationHandler ihlali UniqueConstraintException
        // üzerinden "seat.taken" hatasına çevirir.
        builder.HasIndex(x => x.EventSeatId)
            .IsUnique()
            .HasFilter("is_active = true")
            .HasDatabaseName("ux_reservation_items_active_seat");

        builder.HasIndex(x => x.ReservationId)
            .HasDatabaseName("ix_reservation_items_reservation");
    }
}
