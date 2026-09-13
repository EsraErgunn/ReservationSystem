using Microsoft.Extensions.Logging;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Payments;

/// <summary>
/// api-katmani.md §8. iyzico callback'i tarayıcı redirect'i olduğu için, callback
/// sırasında bir hata olursa (ağ kopması, <c>VerifyAsync</c> zaman aşımı) sağlayıcı
/// tarafından tekrar denenmez. Ödeme <c>Pending</c> kalır: para çekilmiş, bilet
/// verilmemiş olur. Bu komut o kayıtları yakalar.
/// </summary>
/// <param name="MinimumAge">
/// Bu yaştan genç <c>Pending</c> kayıtlara dokunulmaz — kullanıcı hâlâ sağlayıcının
/// ödeme formunu dolduruyor olabilir.
/// </param>
public record ReconcilePendingPaymentsCommand(
    TimeSpan MinimumAge,
    int BatchSize = 50) : ICommand<int>;

/// <summary>
/// Doğrulama/onay mantığı tekrar yazılmıyor: her token için
/// <see cref="CompletePaymentHandler"/> çağrılıyor. O handler BR-12 gereği zaten
/// idempotent — callback bu arada gelip ödemeyi kapattıysa ikinci çağrı sağlayıcıya
/// hiç gitmeden döner.
/// </summary>
public class ReconcilePendingPaymentsHandler(
    IReservationRepository reservations,
    ICommandHandler<CompletePaymentCommand, PaymentCompletionResult> complete,
    TimeProvider clock,
    ILogger<ReconcilePendingPaymentsHandler> logger)
    : ICommandHandler<ReconcilePendingPaymentsCommand, int>
{
    public async Task<int> HandleAsync(
        ReconcilePendingPaymentsCommand command, CancellationToken ct)
    {
        var olderThan = clock.GetUtcNow().UtcDateTime - command.MinimumAge;

        var tokens = await reservations.GetPendingPaymentTokensAsync(
            olderThan, command.BatchSize, ct);

        if (tokens.Count == 0) return 0;

        var processed = 0;

        foreach (var token in tokens)
        {
            try
            {
                var result = await complete.HandleAsync(new CompletePaymentCommand(token), ct);
                processed++;

                logger.LogInformation(
                    "Mutabakat: rezervasyon={ReservationId} onaylandi={Confirmed} mesaj={Message}",
                    result.ReservationId, result.Confirmed, result.Message);
            }
            catch (Exception ex)
            {
                // Tek bir kaydın hatası turu bitirmemeli; kayıt Pending kalır ve
                // sonraki turda tekrar denenir.
                logger.LogError(ex, "Ödeme mutabakatı başarısız. Token={Token}", token);
            }
        }

        return processed;
    }
}
