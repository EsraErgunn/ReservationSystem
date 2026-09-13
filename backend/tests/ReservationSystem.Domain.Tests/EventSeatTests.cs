using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Tests;

public class EventSeatTests
{
    private static EventSeat NewSeat(decimal price = 250m) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), price);

    [Fact]
    public void Ctor_WithNegativePrice_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => NewSeat(-1m));
        Assert.Equal("seat.invalid_price", ex.Code);
    }

    [Fact]
    public void Ctor_StartsAvailable()
    {
        Assert.Equal(SeatStatus.Available, NewSeat().Status);
    }

    [Fact]
    public void Hold_WhenAlreadyHeld_Throws()
    {
        var seat = NewSeat();
        seat.Hold();

        // BR-04
        var ex = Assert.Throws<DomainException>(seat.Hold);
        Assert.Equal("seat.not_available", ex.Code);
    }

    [Fact]
    public void Release_WhenSold_Throws()
    {
        var seat = NewSeat();
        seat.Hold();
        seat.MarkSold();

        // Satılmış koltuğun serbest bırakılması = aynı koltuğun iki kez satılması
        var ex = Assert.Throws<DomainException>(seat.Release);
        Assert.Equal("seat.already_sold", ex.Code);
        Assert.Equal(SeatStatus.Sold, seat.Status);
    }

    [Fact]
    public void MarkSold_WhenNotHeld_Throws()
    {
        var seat = NewSeat();

        var ex = Assert.Throws<DomainException>(seat.MarkSold);
        Assert.Equal("seat.not_held", ex.Code);
    }

    [Fact]
    public void HoldThenRelease_ReturnsToAvailable()
    {
        var seat = NewSeat();
        seat.Hold();
        seat.Release();

        Assert.Equal(SeatStatus.Available, seat.Status);
    }
}
