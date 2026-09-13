using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Domain.Tests;

public class EventTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ctor_WithBlankTitle_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => new Event(
            Guid.CreateVersion7(), "   ", Now.AddDays(30), Now, Now.AddDays(20)));

        Assert.Equal("event.invalid_title", ex.Code);
    }

    [Fact]
    public void Ctor_WithSalesStartAfterEnd_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => new Event(
            Guid.CreateVersion7(), "Konser", Now.AddDays(30), Now.AddDays(20), Now.AddDays(5)));

        Assert.Equal("event.invalid_sales_window", ex.Code);
    }

    [Fact]
    public void Ctor_WithSalesEndingAfterEventDate_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => new Event(
            Guid.CreateVersion7(), "Konser", Now.AddDays(30), Now, Now.AddDays(31)));

        Assert.Equal("event.sales_after_event", ex.Code);
    }

    [Theory]
    [InlineData(-2, false)]  // satış başlamadan önce
    [InlineData(0, true)]    // tam başlangıç anı — sınır dahil
    [InlineData(5, true)]
    [InlineData(20, true)]   // tam bitiş anı — sınır dahil
    [InlineData(21, false)]  // satış kapandıktan sonra
    public void IsOnSale_RespectsWindowBoundaries(int dayOffset, bool expected)
    {
        var salesStart = Now;
        var @event = new Event(
            Guid.CreateVersion7(), "Konser", Now.AddDays(30), salesStart, Now.AddDays(20));

        Assert.Equal(expected, @event.IsOnSale(salesStart.AddDays(dayOffset)));
    }
}

public class SeatTests
{
    [Fact]
    public void Ctor_WithNonPositiveNumber_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Seat(Guid.CreateVersion7(), "A", 0));

        Assert.Equal("seat.invalid_number", ex.Code);
    }

    [Fact]
    public void Label_CombinesRowAndNumber()
    {
        var seat = new Seat(Guid.CreateVersion7(), "B", 12);
        Assert.Equal("B-12", seat.Label);
    }
}

public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ctor_NormalizesEmail()
    {
        var user = new User("  Esra@Example.COM ", "hash", "Esra E.", Now);

        Assert.Equal("esra@example.com", user.Email);
        Assert.Equal(Domain.Enums.UserRole.User, user.Role);
        Assert.Equal(Now, user.CreatedAt);
    }
}
