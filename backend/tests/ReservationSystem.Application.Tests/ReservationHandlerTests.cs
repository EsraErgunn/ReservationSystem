using NSubstitute;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Application.Tests;

public class CreateReservationHandlerTests
{
    private static CreateReservationHandler Build(TestScenario s) => new(
        s.Events, s.Seats, s.Reservations, s.Uow, s.CurrentUser, s.Notifier, s.Clock);

    private static CreateReservationCommand CommandFor(TestScenario s) =>
        new(s.Event.Id, s.EventSeats.Select(x => x.Id).ToList());

    [Fact]
    public async Task HoldsSeatsAndReturnsDto()
    {
        var s = new TestScenario(seatCount: 2);

        var dto = await Build(s).HandleAsync(CommandFor(s), default);

        Assert.Equal("Held", dto.Status);
        Assert.Equal(s.Event.Id, dto.EventId);
        Assert.Equal(250m + 251m, dto.TotalAmount);
        Assert.Equal(2, dto.Items.Count);
        Assert.All(s.EventSeats, seat => Assert.Equal(SeatStatus.Held, seat.Status));

        s.Reservations.Received(1).Add(Arg.Any<Reservation>());
        await s.Notifier.Received(1).NotifySeatsChangedAsync(
            s.Event.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenNotAuthenticated_Throws()
    {
        var s = new TestScenario();
        s.CurrentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            Build(s).HandleAsync(CommandFor(s), default));
    }

    [Fact]
    public async Task WhenEventMissing_Throws()
    {
        var s = new TestScenario();
        s.Events.GetByIdAsync(s.Event.Id, Arg.Any<CancellationToken>()).Returns((Event?)null);

        var ex = await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Build(s).HandleAsync(CommandFor(s), default));

        Assert.Equal("not_found", ex.Code);
    }

    [Fact]
    public async Task WhenSomeSeatsMissing_Throws()
    {
        var s = new TestScenario(seatCount: 1);

        // İstemci 2 koltuk istedi, repository 1 tane buldu
        var command = new CreateReservationCommand(
            s.Event.Id, [s.EventSeats[0].Id, Guid.CreateVersion7()]);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Build(s).HandleAsync(command, default));
    }

    // ---- BR-06: sessiz başarısızlık yok ------------------------------------

    [Fact]
    public async Task WhenConcurrencyConflict_ThrowsSeatTaken()
    {
        var s = new TestScenario();
        s.Uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin mismatch"));

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() =>
            Build(s).HandleAsync(CommandFor(s), default));

        Assert.Equal("seat.taken", ex.Code);

        // Kayıt olmadı — bildirim de gitmemeli
        await s.Notifier.DidNotReceive().NotifySeatsChangedAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenActiveSeatUniqueViolation_ThrowsSeatTaken()
    {
        var s = new TestScenario();
        s.Uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new UniqueConstraintException("ux_reservation_items_active_seat"));

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() =>
            Build(s).HandleAsync(CommandFor(s), default));

        Assert.Equal("seat.taken", ex.Code);
    }
}

public class CancelReservationHandlerTests
{
    private static CancelReservationHandler Build(TestScenario s) => new(
        s.Reservations, s.Seats, s.Uow, s.CurrentUser, s.Notifier, s.Clock);

    [Fact]
    public async Task ReleasesSeatsAndNotifies()
    {
        var s = new TestScenario(seatCount: 2);
        var reservation = s.GivenHeldReservation();

        var ok = await Build(s).HandleAsync(new CancelReservationCommand(reservation.Id), default);

        Assert.True(ok);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.All(s.EventSeats, seat => Assert.Equal(SeatStatus.Available, seat.Status));
        await s.Notifier.Received(1).NotifySeatsChangedAsync(
            reservation.EventId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAnotherUsersReservation_Throws()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.CurrentUser.UserId.Returns(Guid.CreateVersion7());

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Build(s).HandleAsync(new CancelReservationCommand(reservation.Id), default));

        Assert.Equal(ReservationStatus.Held, reservation.Status);
    }
}

public class ExpireReservationsHandlerTests
{
    private static ExpireReservationsHandler Build(TestScenario s) => new(
        s.Reservations, s.Seats, s.Uow, s.Notifier, s.Clock);

    // ---- BR-02 -------------------------------------------------------------

    [Fact]
    public async Task ReleasesExpiredHoldsAndCountsThem()
    {
        var s = new TestScenario(seatCount: 2);
        var reservation = s.GivenHeldReservation();

        s.Clock.Advance(TimeSpan.FromMinutes(11));
        s.Reservations.GetExpiredAsync(
                Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { reservation });

        var processed = await Build(s).HandleAsync(new ExpireReservationsCommand(), default);

        Assert.Equal(1, processed);
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.All(s.EventSeats, seat => Assert.Equal(SeatStatus.Available, seat.Status));
        Assert.All(reservation.Items, i => Assert.False(i.IsActive));
    }

    [Fact]
    public async Task WhenNothingExpired_DoesNoWork()
    {
        var s = new TestScenario();
        s.Reservations.GetExpiredAsync(
                Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Reservation>());

        var processed = await Build(s).HandleAsync(new ExpireReservationsCommand(), default);

        Assert.Equal(0, processed);
        await s.Uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Temizlik görevi rezervasyonu okuduktan sonra kullanıcı ödemeyi tamamladı.
    /// Doğru davranış: kullanıcı kazanır, görev sessizce geri çekilir.
    /// </summary>
    [Fact]
    public async Task WhenUserWinsTheRace_SkipsWithoutThrowing()
    {
        var s = new TestScenario();
        var reservation = s.GivenHeldReservation();

        s.Clock.Advance(TimeSpan.FromMinutes(11));
        s.Reservations.GetExpiredAsync(
                Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { reservation });
        s.Uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin mismatch"));

        var processed = await Build(s).HandleAsync(new ExpireReservationsCommand(), default);

        Assert.Equal(0, processed);
        await s.Notifier.DidNotReceive().NotifySeatsChangedAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }
}
