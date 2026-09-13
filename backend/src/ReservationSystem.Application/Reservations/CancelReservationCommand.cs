using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Reservations;

/// <summary>FR-11: kullanıcı ödeme öncesi rezervasyonunu iptal edebilir.</summary>
public record CancelReservationCommand(Guid ReservationId) : ICommand<bool>;

public class CancelReservationHandler(
    IReservationRepository reservations,
    IEventSeatRepository seats,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<CancelReservationCommand, bool>
{
    public async Task<bool> HandleAsync(CancelReservationCommand command, CancellationToken ct)
    {
        // BR-14
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAppException();

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetByIdAsync(command.ReservationId, ct)
            ?? throw new NotFoundAppException("reservation", command.ReservationId);

        // BR-15: kullanıcı yalnızca kendi rezervasyonuna dokunabilir
        if (reservation.UserId != userId && !currentUser.IsAdmin)
            throw new ForbiddenAppException();

        var eventSeats = await seats.GetByReservationAsync(reservation.Id, ct);

        try
        {
            // F maddesi: Reservation + Items + EventSeats tek transaction
            await uow.ExecuteInTransactionAsync(async innerCt =>
            {
                reservation.Cancel(eventSeats, utcNow);
                await uow.SaveChangesAsync(innerCt);
                return true;
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Kullanıcı iptal ederken ödeme/temizlik aynı rezervasyonu kapattı.
            throw new ConflictAppException(
                "reservation.state_changed",
                "Rezervasyonun durumu az önce değişti; sayfayı yenileyin.");
        }

        await notifier.NotifySeatsChangedAsync(
            reservation.EventId, eventSeats.Select(s => s.Id).ToList(), ct);

        return true;
    }
}
