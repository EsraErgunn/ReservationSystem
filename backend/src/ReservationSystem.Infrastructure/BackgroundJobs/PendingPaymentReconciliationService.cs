using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Payments;

namespace ReservationSystem.Infrastructure.BackgroundJobs;

/// <summary>
/// api-katmani.md §8: iyzico callback'i tarayıcı redirect'i olduğu için sağlayıcı
/// tarafından tekrar denenmez. Callback kaybolursa ödeme <c>Pending</c> kalır —
/// para çekilmiş, bilet verilmemiş olur. İş mantığı handler'da; bu sınıf yalnızca
/// zamanlayıcı.
/// </summary>
public class PendingPaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<PendingPaymentReconciliationOptions> options,
    ILogger<PendingPaymentReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.Interval);

        logger.LogInformation(
            "Bekleyen ödeme mutabakatı başladı. Aralık={Interval} AsgariYas={MinimumAge} Batch={BatchSize}",
            settings.Interval, settings.MinimumAge, settings.BatchSize);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ReconcilePendingPaymentsCommand, int>>();

                var count = await handler.HandleAsync(
                    new ReconcilePendingPaymentsCommand(settings.MinimumAge, settings.BatchSize),
                    stoppingToken);

                if (count > 0)
                    logger.LogInformation("{Count} bekleyen ödeme mutabakatı yapıldı.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mutabakat turu başarısız — sonraki turda tekrar denenecek.");
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
