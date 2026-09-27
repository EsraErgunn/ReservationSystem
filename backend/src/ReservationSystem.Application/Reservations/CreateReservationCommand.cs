using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations.Dtos;
using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Reservations;

public record CreateReservationCommand(
    Guid EventId,
    IReadOnlyList<Guid> EventSeatIds) : ICommand<ReservationDto>;

public class CreateReservationHandler(
    IEventRepository events,
    IEventSeatRepository seats,
    IReservationRepository reservations,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ISeatAvailabilityNotifier notifier,
    TimeProvider clock)
    : ICommandHandler<CreateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> HandleAsync(
        CreateReservationCommand command, CancellationToken ct)
    {
        // BR-14
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAppException();

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var @event = await events.GetByIdAsync(command.EventId, ct)
            ?? throw new NotFoundAppException("event", command.EventId);

        var eventSeats = await seats.GetByIdsAsync(command.EventSeatIds, ct);

        if (eventSeats.Count != command.EventSeatIds.Count)
            throw new NotFoundAppException("event_seat", "Bazı koltuklar bulunamadı.");

        // Tüm kurallar burada değil — Domain'de. Application sadece çağırır.
        Reservation reservation;
        try
        {
            reservation = Reservation.Create(userId, @event, eventSeats, utcNow);
        }
        catch (DomainException ex) when (ex.Code == "seat.not_available")
        {
            // BR-06: koltuk zaten okunduğu anda doluysa da (sıralı istek) yarış
            // durumundaki (eşzamanlı istek) ile aynı 409 + aynı mesaj dönmeli —
            // istemci iki durumu ayırt etmek zorunda kalmasın.
            throw SeatTaken();
        }

        reservations.Add(reservation);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // BR-06: başka bir kullanıcı aynı koltuğu bizden önce aldı.
            // Retry YOK — koltuk gerçekten gittiyse tekrar denemek aynı sonucu verir.
            throw SeatTaken();
        }
        catch (UniqueConstraintException)
        {
            // ux_reservation_items_active_seat ihlali — son savunma hattı devreye girdi
            throw SeatTaken();
        }

        // C maddesi: bildirim SaveChanges'ten SONRA, transaction dışında.
        await notifier.NotifySeatsChangedAsync(@event.Id, command.EventSeatIds, ct);

        return ReservationDto.From(reservation);
    }

    private static ConflictAppException SeatTaken() => new(
        "seat.taken",
        "Seçtiğiniz koltuklardan biri az önce başkası tarafından alındı.");
}
