using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats", t => t.HasCheckConstraint(
            "ck_seats_number", "seat_number > 0"));

        builder.HasKey(x => x.Id);

        // row PostgreSQL'de ayrılmış kelime — kolon adı row_label
        builder.Property(x => x.RowLabel).HasMaxLength(8).IsRequired();
        builder.Property(x => x.SeatNumber).IsRequired();

        // Label hesaplanan bir özellik — kolon olarak tutulmaz
        builder.Ignore(x => x.Label);

        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(x => x.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        // Aynı mekânda iki kez "A-12" tanımlanamaz
        builder.HasIndex(x => new { x.VenueId, x.RowLabel, x.SeatNumber })
            .IsUnique()
            .HasDatabaseName("ux_seats_venue_position");
    }
}
