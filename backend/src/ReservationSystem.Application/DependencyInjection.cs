using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ReservationSystem.Application.Auth;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments;
using ReservationSystem.Application.Reservations;
using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Handler'lar bilerek elle kaydediliyor — DI'ın ne yaptığı görünür kalsın diye.
    /// Liste rahatsız edici uzunluğa gelince Scrutor ile assembly taramasına geçilir.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<CreateReservationCommand, ReservationDto>,
            CreateReservationHandler>();

        services.AddScoped<
            ICommandHandler<CancelReservationCommand, bool>,
            CancelReservationHandler>();

        services.AddScoped<
            ICommandHandler<ExpireReservationsCommand, int>,
            ExpireReservationsHandler>();

        services.AddScoped<
            ICommandHandler<StartPaymentCommand, StartPaymentResult>,
            StartPaymentHandler>();

        services.AddScoped<
            ICommandHandler<CompletePaymentCommand, PaymentCompletionResult>,
            CompletePaymentHandler>();

        services.AddScoped<
            ICommandHandler<ReconcilePendingPaymentsCommand, int>,
            ReconcilePendingPaymentsHandler>();

        services.AddScoped<
            ICommandHandler<RegisterCommand, AuthResult>,
            RegisterHandler>();

        services.AddScoped<
            ICommandHandler<LoginCommand, AuthResult>,
            LoginHandler>();

        services.AddScoped<IValidator<RegisterCommand>, RegisterValidator>();

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
