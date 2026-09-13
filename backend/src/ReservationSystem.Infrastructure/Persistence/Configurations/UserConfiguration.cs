using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_role", "role IN ('User', 'Admin')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // ux_users_email BİLEREK burada tanımlı değil: şema onu lower(email)
        // üzerinde istiyor ve EF Core ifade tabanlı (functional) index'i modelde
        // ifade edemiyor. Index, InitialCreate migration'ında ham SQL ile
        // oluşturuluyor. Domain ctor'u zaten normalize ediyor; bu index
        // Esra@x.com / esra@x.com ikiliğine karşı ikinci savunma hattı.
    }
}
