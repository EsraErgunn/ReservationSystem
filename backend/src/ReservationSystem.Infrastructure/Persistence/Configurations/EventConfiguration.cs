using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // BR-17'yi veritabanı seviyesinde destekler
        builder.ToTable("events", t =>
        {
            t.HasCheckConstraint("ck_events_sales_window", "sales_start_at < sales_end_at");
            t.HasCheckConstraint("ck_events_sales_before_event", "sales_end_at <= event_date");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.EventDate).IsRequired();
        builder.Property(x => x.SalesStartAt).IsRequired();
        builder.Property(x => x.SalesEndAt).IsRequired();

        // Şemada var, Domain entity'sinde yok — shadow property olarak eklenip
        // değeri veritabanı tarafından üretiliyor.
        builder.Property<DateTime>("CreatedAt")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        // ON DELETE RESTRICT: etkinliği olan mekân silinemez. CASCADE, satılmış
        // biletleri sessizce yok etmek olurdu.
        builder.HasOne<Venue>()
            .WithMany()
            .HasForeignKey(x => x.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Event.EventSeats))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.EventDate).HasDatabaseName("ix_events_date");
    }
}
