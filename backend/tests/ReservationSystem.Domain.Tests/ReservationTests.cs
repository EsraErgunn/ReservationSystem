using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;
using ReservationSystem.Domain.Rules;

namespace ReservationSystem.Domain.Tests;

public class ReservationTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Satışı açık bir etkinlik ve istenen sayıda müsait koltuk üretir.</summary>
    private static (Event Event, EventSeat[] Seats) BuildOnSaleEvent(DateTime utcNow, int seatCount = 1)
    {
        var @event = new Event(
            venueId: Guid.CreateVersion7(),
            title: "Konser",
            eventDate: utcNow.AddDays(30),
            salesStartAt: utcNow.AddDays(-1),
            salesEndAt: utcNow.AddDays(20));

        var seats = Enumerable
            .Range(0, seatCount)
            .Select(i => new EventSeat(@event.Id, Guid.CreateVersion7(), 250m + i))
            .ToArray();

        return (@event, seats);
    }

    // ---- BR-17: satış penceresi --------------------------------------------

    [Fact]
    public void Create_WhenSalesClosed_Throws()
    {
        var @event = new Event(
            venueId: Guid.CreateVersion7(),
            title: "Konser",
            eventDate: Now.AddDays(30),
            salesStartAt: Now.AddDays(1),      // satış henüz başlamadı
            salesEndAt: Now.AddDays(20));

        var seat = new EventSeat(@event.Id, Guid.CreateVersion7(), 250m);

        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, [seat], Now));

        Assert.Equal("reservation.sales_closed", ex.Code);
    }

    // ---- BR-01 / BR-03: hold süresi ve koltuk limiti ------------------------

    [Fact]
    public void Create_SetsHoldWindowAndTotal()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: 2);

        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Equal(Now.Add(ReservationRules.HoldDuration), reservation.HeldUntil);
        Assert.Equal(250m + 251m, reservation.TotalAmount);
        Assert.All(seats, s => Assert.Equal(SeatStatus.Held, s.Status));
    }

    [Fact]
    public void Create_WithNoSeats_Throws()
    {
        var (@event, _) = BuildOnSaleEvent(Now);

        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, [], Now));

        Assert.Equal("reservation.no_seats", ex.Code);
    }

    [Fact]
    public void Create_WithTooManySeats_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: ReservationRules.MaxSeatsPerReservation + 1);

        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, seats, Now));

        Assert.Equal("reservation.too_many_seats", ex.Code);
    }

    [Fact]
    public void Create_WithDuplicateSeat_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var seat = seats[0];

        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, [seat, seat], Now));

        Assert.Equal("reservation.duplicate_seat", ex.Code);
    }

    [Fact]
    public void Create_WithSeatFromAnotherEvent_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var foreignSeat = new EventSeat(Guid.CreateVersion7(), Guid.CreateVersion7(), 100m);

        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, [seats[0], foreignSeat], Now));

        Assert.Equal("reservation.seat_event_mismatch", ex.Code);
    }

    [Fact]
    public void Create_WhenSeatAlreadyHeld_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        // BR-04: aynı koltuk için ikinci aktif hold olamaz
        var ex = Assert.Throws<DomainException>(() =>
            Reservation.Create(Guid.CreateVersion7(), @event, seats, Now));

        Assert.Equal("seat.not_available", ex.Code);
    }

    // ---- Confirm ------------------------------------------------------------

    [Fact]
    public void Confirm_MarksSeatsSold()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: 2);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        reservation.Confirm(seats, Now.AddMinutes(3));

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.All(seats, s => Assert.Equal(SeatStatus.Sold, s.Status));
    }

    [Fact]
    public void Confirm_AfterHoldExpired_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        // 11 dakika sonra — hold süresi 10 dakika
        var later = Now.AddMinutes(11);

        var ex = Assert.Throws<DomainException>(() => reservation.Confirm(seats, later));

        Assert.Equal("reservation.expired", ex.Code);
    }

    [Fact]
    public void Confirm_WithMismatchedSeats_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: 2);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        var ex = Assert.Throws<DomainException>(() =>
            reservation.Confirm([seats[0]], Now.AddMinutes(1)));

        Assert.Equal("reservation.seat_mismatch", ex.Code);
    }

    [Fact]
    public void Confirm_Twice_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);
        reservation.Confirm(seats, Now.AddMinutes(1));

        var ex = Assert.Throws<DomainException>(() =>
            reservation.Confirm(seats, Now.AddMinutes(2)));

        Assert.Equal("reservation.invalid_state", ex.Code);
    }

    // ---- Expire / Cancel / Failed ------------------------------------------

    [Fact]
    public void Expire_ReleasesSeatsAndDeactivatesItems()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: 2);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        reservation.Expire(seats, Now.AddMinutes(11));

        Assert.Equal(ReservationStatus.Expired, reservation.Status);
        Assert.All(seats, s => Assert.Equal(SeatStatus.Available, s.Status));
        Assert.All(reservation.Items, i => Assert.False(i.IsActive));
    }

    [Fact]
    public void Cancel_ReleasesSeats()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        reservation.Cancel(seats, Now.AddMinutes(2));

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(SeatStatus.Available, seats[0].Status);
    }

    [Fact]
    public void MarkFailed_ReleasesSeats()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        reservation.MarkFailed(seats, Now.AddMinutes(2));

        Assert.Equal(ReservationStatus.Failed, reservation.Status);
        Assert.Equal(SeatStatus.Available, seats[0].Status);
    }

    [Fact]
    public void IsExpired_OnlyWhileHeldAndPastDeadline()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        Assert.False(reservation.IsExpired(Now.AddMinutes(9)));
        Assert.True(reservation.IsExpired(Now.AddMinutes(11)));

        reservation.Confirm(seats, Now.AddMinutes(1));
        Assert.False(reservation.IsExpired(Now.AddMinutes(11)));
    }

    // ---- StartPayment -------------------------------------------------------

    [Fact]
    public void StartPayment_UsesServerCalculatedTotal()
    {
        var (@event, seats) = BuildOnSaleEvent(Now, seatCount: 2);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        var payment = reservation.StartPayment(Now.AddMinutes(1));

        // BR-09: tutar sunucuda hesaplanır
        Assert.Equal(reservation.TotalAmount, payment.Amount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void StartPayment_SecondCall_AbandonsFirst()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        var first = reservation.StartPayment(Now.AddMinutes(1));
        var second = reservation.StartPayment(Now.AddMinutes(2));

        Assert.Equal(PaymentStatus.Abandoned, first.Status);
        Assert.Equal(PaymentStatus.Pending, second.Status);
        Assert.Equal(2, reservation.Payments.Count);
    }

    [Fact]
    public void StartPayment_AfterHoldExpired_Throws()
    {
        var (@event, seats) = BuildOnSaleEvent(Now);
        var reservation = Reservation.Create(Guid.CreateVersion7(), @event, seats, Now);

        var ex = Assert.Throws<DomainException>(() =>
            reservation.StartPayment(Now.AddMinutes(11)));

        Assert.Equal("reservation.expired", ex.Code);
    }
}
