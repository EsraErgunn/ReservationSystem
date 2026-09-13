using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations;

namespace ReservationSystem.Infrastructure.BackgroundJobs;

/// <summary>
/// BR-02: hold süresi dolduğunda koltuk, kullanıcı hiçbir aksiyon almasa bile
/// serbest bırakılmalı. İş mantığı handler'da; bu sınıf yalnızca zamanlayıcı.
/// </summary>
public class ExpiredReservationCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExpiredReservationCleanupOptions> options,
    ILogger<ExpiredReservationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.Interval);

        logger.LogInformation(
            "Süresi dolan rezervasyon temizliği başladı. Aralık={Interval} Batch={BatchSize}",
            settings.Interval, settings.BatchSize);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                // Handler scoped (DbContext ile aynı ömür) — her tur kendi scope'unda.
                using var scope = scopeFactory.CreateScope();

                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ExpireReservationsCommand, int>>();

                var processed = await handler.HandleAsync(
                    new ExpireReservationsCommand(settings.BatchSize), stoppingToken);

                if (processed > 0)
                    logger.LogInformation("{Count} rezervasyonun süresi doldu, koltuklar serbest bırakıldı.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Tek turun hatası servisi düşürmemeli — sonraki turda tekrar denenir.
                logger.LogError(ex, "Rezervasyon temizlik turu başarısız oldu.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
