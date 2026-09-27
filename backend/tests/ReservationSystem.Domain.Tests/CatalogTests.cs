using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Tests;

/// <summary>FR-12: mekân düzeni ve etkinlik koltuklarının üretimi.</summary>
public class CatalogTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Venue NewVenue() => new("Salon", "Adres", "İstanbul");

    private static Event NewEvent(Guid venueId) =>
        new(venueId, "Konser", Now.AddDays(30), Now, Now.AddDays(20));

    [Fact]
    public void CreateLayout_ProducesNumberedSeatsPerRow()
    {
        var venue = NewVenue();

        var seats = venue.CreateLayout([("a", 3), ("B", 2)]);

        Assert.Equal(5, seats.Count);
        Assert.All(seats, s => Assert.Equal(venue.Id, s.VenueId));
        Assert.Equal(["A-1", "A-2", "A-3", "B-1", "B-2"], seats.Select(s => s.Label));
    }

    [Fact]
    public void CreateLayout_WithDuplicateRowLabel_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            NewVenue().CreateLayout([("A", 3), ("a", 2)]));

        Assert.Equal("venue.duplicate_row", ex.Code);
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData(" ", 5)]
    public void CreateLayout_WithInvalidRow_Throws(string label, int count)
    {
        var ex = Assert.Throws<DomainException>(() =>
            NewVenue().CreateLayout([(label, count)]));

        Assert.Equal("venue.invalid_row", ex.Code);
    }

    [Fact]
    public void CreateLayout_WithNoRows_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => NewVenue().CreateLayout([]));

        Assert.Equal("venue.empty_layout", ex.Code);
    }

    [Fact]
    public void Venue_WithBlankName_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => new Venue(" ", "Adres", "Şehir"));

        Assert.Equal("venue.invalid_name", ex.Code);
    }

    [Fact]
    public void CreateSeats_CreatesAvailableSeatForEachVenueSeatWithGivenPrice()
    {
        var venue = NewVenue();
        var layout = venue.CreateLayout([("A", 2), ("B", 2)]);
        var @event = NewEvent(venue.Id);

        var eventSeats = @event.CreateSeats(layout, s => s.RowLabel == "A" ? 500m : 300m);

        Assert.Equal(4, eventSeats.Count);
        Assert.All(eventSeats, s =>
        {
            Assert.Equal(@event.Id, s.EventId);
            Assert.Equal(SeatStatus.Available, s.Status);
        });
        Assert.Equal([500m, 500m, 300m, 300m], eventSeats.Select(s => s.Price));
        Assert.Equal(4, @event.EventSeats.Count);
    }

    [Fact]
    public void CreateSeats_WithSeatsOfAnotherVenue_Throws()
    {
        var layout = NewVenue().CreateLayout([("A", 2)]);
        var @event = NewEvent(Guid.CreateVersion7());

        var ex = Assert.Throws<DomainException>(() => @event.CreateSeats(layout, _ => 100m));

        Assert.Equal("event.seat_venue_mismatch", ex.Code);
    }

    [Fact]
    public void CreateSeats_CalledTwice_Throws()
    {
        var venue = NewVenue();
        var layout = venue.CreateLayout([("A", 2)]);
        var @event = NewEvent(venue.Id);
        @event.CreateSeats(layout, _ => 100m);

        var ex = Assert.Throws<DomainException>(() => @event.CreateSeats(layout, _ => 100m));

        Assert.Equal("event.seats_already_created", ex.Code);
    }

    [Fact]
    public void CreateSeats_WithEmptyVenue_Throws()
    {
        var @event = NewEvent(Guid.CreateVersion7());

        var ex = Assert.Throws<DomainException>(() => @event.CreateSeats([], _ => 100m));

        Assert.Equal("event.no_seats", ex.Code);
    }

    [Fact]
    public void Event_TrimsTitleAndStoresDescription()
    {
        var @event = new Event(Guid.CreateVersion7(), "  Konser ", Now.AddDays(30), Now, Now.AddDays(20), "  Açıklama ");

        Assert.Equal("Konser", @event.Title);
        Assert.Equal("Açıklama", @event.Description);
    }

    [Fact]
    public void User_PromoteToAdmin_SetsAdminRole()
    {
        var user = new User("admin@example.com", "hash", "Admin", Now);
        Assert.Equal(UserRole.User, user.Role);

        user.PromoteToAdmin();

        Assert.Equal(UserRole.Admin, user.Role);
    }
}
