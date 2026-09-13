using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(512).IsRequired();
        builder.Property(x => x.City).HasMaxLength(64).IsRequired();
    }
}
