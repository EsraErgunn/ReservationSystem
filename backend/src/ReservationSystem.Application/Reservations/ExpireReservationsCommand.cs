using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Reservations;

/// <summary>
/// BR-02. Infrastructure'daki <c>BackgroundService</c> bunu periyodik çağırır.
/// </summary>
public record ExpireReservationsCommand(int BatchSize = 100) : ICommand<int>;

public class ExpireReservationsHandler(
    IReservationRepository reservations,
    IEventSeatRepository seats,
    IUnitOfWork uow,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<ExpireReservationsCommand, int>
{
    public async Task<int> HandleAsync(ExpireReservationsCommand command, CancellationToken ct)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var expired = await reservations.GetExpiredAsync(utcNow, command.BatchSize, ct);
        if (expired.Count == 0) return 0;

        var processed = 0;

        // Tek tek, ExecuteUpdate ile toplu DEĞİL: toplu güncelleme domain kurallarını
        // atlar ve xmin concurrency kontrolünü devre dışı bırakır. Doğruluk > hız.
        foreach (var reservation in expired)
        {
            var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

            try
            {
                await uow.ExecuteInTransactionAsync(async innerCt =>
                {
                    reservation.Expire(eventSeats, utcNow);
                    await uow.SaveChangesAsync(innerCt);
                    return true;
                }, ct);

                await notifier.NotifySeatsChangedAsync(
                    reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

                processed++;
            }
            catch (ConcurrencyConflictException)
            {
                // Kullanıcı tam o anda ödemeyi tamamladı ve rezervasyon Confirmed oldu.
                // Bu bir hata değil — kullanıcı kazanır, temizlik görevi geri çekilir.
            }
        }

        return processed;
    }
}
