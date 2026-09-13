using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReservationSystem.Infrastructure.Persistence;

/// <summary>
/// Yalnızca tasarım zamanı (<c>dotnet ef migrations</c>) için. Api projesi
/// ayağa kalkmadan bu katmanda migration üretilebilsin diye var; çalışma
/// zamanındaki DbContext DI üzerinden, gerçek connection string ile gelir.
///
/// Migration üretimi veritabanına bağlanmaz — aşağıdaki adres yalnızca
/// sağlayıcıyı seçmek için gerekli.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Port=5432;Database=reservation;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? DesignTimeConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
