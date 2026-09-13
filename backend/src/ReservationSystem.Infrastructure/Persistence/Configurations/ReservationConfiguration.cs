using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations", t =>
        {
            t.HasCheckConstraint("ck_reservations_status",
                "status IN ('Held', 'Confirmed', 'Expired', 'Failed', 'Cancelled')");
            t.HasCheckConstraint("ck_reservations_total", "total_amount >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.HeldUntil).IsRequired();
        builder.Property(x => x.TotalAmount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // Temizlik görevi ile kullanıcının ödemesi aynı satırda yarışabilir
        // (ExpireReservationsHandler bu çakışmayı yakalayıp yutuyor).
        builder.UseXminConcurrencyToken();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // event_id bilinçli denormalizasyon: "bu etkinliğin rezervasyonları"
        // sorgusu her seferinde join gerektirmesin.
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aggregate koleksiyonları backing field üzerinden — setter'lar private
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mali kayıtlar asla cascade ile silinmez
        builder.HasMany(x => x.Payments)
            .WithOne()
            .HasForeignKey(p => p.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Reservation.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Reservation.Payments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // BR-02: temizlik görevi saniyede bir çalışıyor. Partial index sayesinde
        // milyonlarca Confirmed satırı değil, yalnızca aktif hold'lar taranır.
        builder.HasIndex(x => x.HeldUntil)
            .HasFilter("status = 'Held'")
            .HasDatabaseName("ix_reservations_expiry");

        // FR-10: kullanıcının bilet geçmişi, en yeniden eskiye
        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_reservations_user");
    }
}
