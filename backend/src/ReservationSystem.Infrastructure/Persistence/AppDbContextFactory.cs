using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ReservationSystem.Infrastructure.Persistence;

/// <summary>
/// Yalnızca tasarım zamanı (<c>dotnet ef migrations</c> / <c>database update</c>)
/// için. Api projesi ayağa kalkmadan bu katmanda migration üretilebilsin diye var;
/// çalışma zamanındaki DbContext DI üzerinden gelir.
///
/// Bağlantı dizesinin tek kaynağı user-secrets'tır (bkz. docs/api-katmani.md §9). Buraya
/// sabit bir dize yazılmaz: <c>dotnet ef</c> launchSettings.json'ı uygulamadığı
/// için ortam Production sayılır ve appsettings.Development.json yüklenmez —
/// sabit bir fallback bu durumda sessizce yanlış veritabanına bağlanır.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // UserSecretsId csproj'da Api ile aynı: iki proje de tek secret deposunu okur.
        // Ortam değişkeni (ConnectionStrings__Postgres) CI ve Docker için üstte kalır.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres tanımlı değil. Şu komutla verin:\n" +
                "  dotnet user-secrets set \"ConnectionStrings:Postgres\" " +
                "\"Host=localhost;Port=5433;Database=reservationdb;Username=reservation;Password=localdev123\" " +
                "--project src/ReservationSystem.Api\n" +
                "veya ConnectionStrings__Postgres ortam değişkenini ayarlayın.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
