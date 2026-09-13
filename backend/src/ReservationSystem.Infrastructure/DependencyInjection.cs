using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;
using ReservationSystem.Application.Payments;
using ReservationSystem.Infrastructure.BackgroundJobs;
using ReservationSystem.Infrastructure.Identity;
using ReservationSystem.Infrastructure.Payments;
using ReservationSystem.Infrastructure.Persistence;
using ReservationSystem.Infrastructure.Persistence.Queries;
using ReservationSystem.Infrastructure.Persistence.Repositories;
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

        // Okuma tarafı: domain'den geçmeyen projeksiyon sorgusu
        services.AddScoped<IQueryHandler<GetSeatMapQuery, SeatMapDto>, GetSeatMapQueryHandler>();
        services.AddScoped<IReservationQueries, ReservationQueries>();

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
        services.AddSingleton<IPaymentGateway, IyzicoPaymentGateway>();

        services.Configure<ExpiredReservationCleanupOptions>(
            configuration.GetSection(ExpiredReservationCleanupOptions.SectionName));
        services.AddHostedService<ExpiredReservationCleanupService>();

        services.Configure<PendingPaymentReconciliationOptions>(
            configuration.GetSection(PendingPaymentReconciliationOptions.SectionName));
        services.AddHostedService<PendingPaymentReconciliationService>();

        return services;
    }
}
