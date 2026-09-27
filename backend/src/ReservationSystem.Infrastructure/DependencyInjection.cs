using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;
using ReservationSystem.Application.Payments;
using ReservationSystem.Application.Venues;
using ReservationSystem.Infrastructure.Caching;
using ReservationSystem.Infrastructure.BackgroundJobs;
using ReservationSystem.Infrastructure.Identity;
using ReservationSystem.Infrastructure.Payments;
using ReservationSystem.Infrastructure.Persistence;
using ReservationSystem.Infrastructure.Persistence.Queries;
using ReservationSystem.Infrastructure.Persistence.Repositories;
using ReservationSystem.Infrastructure.Persistence.Seed;
using ReservationSystem.Infrastructure.RealTime;

namespace ReservationSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Tek kaynak: user-secrets (Development) veya ConnectionStrings__Postgres
        // ortam degiskeni (Docker/CI). Sabit fallback yok - yanlis veritabanina
        // sessizce baglanmaktansa acilista patlamak yeglenir.
        var postgres = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(postgres))
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres yapilandirilmamis. user-secrets veya " +
                "ConnectionStrings__Postgres ortam degiskeni ile verin.");

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(postgres, npgsql => npgsql.EnableRetryOnFailure())
               .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IEventSeatRepository, EventSeatRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        // Okuma tarafı: domain'den geçmeyen projeksiyon sorgusu
        services.AddScoped<IQueryHandler<GetSeatMapQuery, SeatMapDto>, GetSeatMapQueryHandler>();
        services.AddScoped<IReservationQueries, ReservationQueries>();
        services.AddScoped<IQueryHandler<GetEventsQuery, IReadOnlyList<EventSummaryDto>>, GetEventsQueryHandler>();
        services.AddScoped<IQueryHandler<GetEventByIdQuery, EventDetailDto>, GetEventByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetEventSalesQuery, EventSalesDto>, GetEventSalesQueryHandler>();
        services.AddScoped<IQueryHandler<GetVenuesQuery, IReadOnlyList<VenueDto>>, GetVenuesQueryHandler>();

        // NFR-02: Redis verilmişse dağıtık cache, verilmemişse (ör. testler) bellek içi.
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
            services.AddDistributedMemoryCache();
        else
            services.AddStackExchangeRedisCache(opt =>
            {
                opt.Configuration = redis;
                opt.InstanceName = "reservation:";
            });
        services.AddScoped<IEventCatalogCache, EventCatalogCache>();

        // auth.md §5–6. JwtOptions burada Configure ediliyor; Api'nin AddJwtBearer
        // yapılandırması da aynı kaydı okur.
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        // ICurrentUser implementasyonu Api katmanında: HttpContext bir web kavramı,
        // Infrastructure'ın arka plan servisleri onu görmemeli (api-katmani.md §4).

        services.AddSignalR();
        services.AddScoped<ISeatAvailabilityNotifier, SignalRSeatNotifier>();

        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.Configure<IyzicoOptions>(configuration.GetSection(IyzicoOptions.SectionName));
        AddPaymentGateway(services, configuration);

        services.Configure<ExpiredReservationCleanupOptions>(
            configuration.GetSection(ExpiredReservationCleanupOptions.SectionName));
        services.AddHostedService<ExpiredReservationCleanupService>();

        services.Configure<PendingPaymentReconciliationOptions>(
            configuration.GetSection(PendingPaymentReconciliationOptions.SectionName));
        services.AddHostedService<PendingPaymentReconciliationService>();

        return services;
    }

    /// <summary>
    /// <c>Payment:Provider</c> = <c>Iyzico</c> | <c>Fake</c>. Belirtilmezse iyzico
    /// anahtarı varsa iyzico, yoksa sahte sağlayıcı seçilir — anahtarsız yerel
    /// kurulumda da uçtan uca akış çalışsın diye.
    /// </summary>
    private static void AddPaymentGateway(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Payment:Provider"];
        var hasIyzicoKey = !string.IsNullOrWhiteSpace(configuration["Iyzico:ApiKey"]);

        var useFake = string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase)
                      || (string.IsNullOrWhiteSpace(provider) && !hasIyzicoKey);

        if (useFake)
        {
            // Aynı singleton hem port hem somut tip olarak: fake-checkout ucu sonucu
            // TrySetOutcome ile bu örneğe yazar.
            services.AddSingleton<FakePaymentGateway>();
            services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());
        }
        else
        {
            services.AddSingleton<IPaymentGateway, IyzicoPaymentGateway>();
        }
    }
}
