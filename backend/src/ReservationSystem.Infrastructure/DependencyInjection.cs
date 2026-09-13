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
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.EnableRetryOnFailure())
               .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IEventSeatRepository, EventSeatRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Okuma tarafı: domain'den geçmeyen projeksiyon sorgusu
        services.AddScoped<IQueryHandler<GetSeatMapQuery, SeatMapDto>, GetSeatMapQueryHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddSignalR();
        services.AddScoped<ISeatAvailabilityNotifier, SignalRSeatNotifier>();

        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.Configure<IyzicoOptions>(configuration.GetSection(IyzicoOptions.SectionName));
        services.AddSingleton<IPaymentGateway, IyzicoPaymentGateway>();

        services.Configure<ExpiredReservationCleanupOptions>(
            configuration.GetSection(ExpiredReservationCleanupOptions.SectionName));
        services.AddHostedService<ExpiredReservationCleanupService>();

        return services;
    }
}
