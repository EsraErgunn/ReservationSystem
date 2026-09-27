using FluentValidation;
using NSubstitute;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;
using ReservationSystem.Application.Venues;
using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Tests;

/// <summary>FR-12 / BR-16: mekân ve etkinlik tanımlama.</summary>
public class AdminHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly IVenueRepository _venues = Substitute.For<IVenueRepository>();
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEventCatalogCache _cache = Substitute.For<IEventCatalogCache>();

    public AdminHandlerTests()
    {
        _currentUser.UserId.Returns(Guid.CreateVersion7());
        _currentUser.IsAdmin.Returns(true);
    }

    private CreateVenueHandler VenueHandler() =>
        new(_venues, _uow, _currentUser, new CreateVenueValidator());

    private CreateEventHandler EventHandler() =>
        new(_venues, _events, _uow, _currentUser, _cache, new CreateEventValidator());

    private static CreateVenueCommand VenueCommand(params VenueRowInput[] rows) =>
        new("Salon", "Adres", "İstanbul", rows.Length == 0 ? [new VenueRowInput("A", 10)] : rows);

    private CreateEventCommand EventCommand(Guid venueId, IReadOnlyList<RowPriceInput>? rowPrices = null) =>
        new(venueId, "Konser", "Açıklama",
            EventDate: Now.AddDays(30),
            SalesStartAt: Now,
            SalesEndAt: Now.AddDays(29),
            DefaultPrice: 300m,
            RowPrices: rowPrices);

    private Venue GivenVenueWithLayout(params (string, int)[] rows)
    {
        var venue = new Venue("Salon", "Adres", "İstanbul");
        var seats = venue.CreateLayout(rows);
        _venues.GetByIdAsync(venue.Id, Arg.Any<CancellationToken>()).Returns(venue);
        _venues.GetSeatsAsync(venue.Id, Arg.Any<CancellationToken>()).Returns(seats);
        return venue;
    }

    // ---------- CreateVenue ----------

    [Fact]
    public async Task CreateVenue_AddsVenueWithGeneratedSeats()
    {
        var result = await VenueHandler().HandleAsync(
            VenueCommand(new VenueRowInput("A", 10), new VenueRowInput("B", 12)), default);

        Assert.Equal(22, result.SeatCount);
        _venues.Received(1).Add(
            Arg.Is<Venue>(v => v.Id == result.Id),
            Arg.Is<IReadOnlyList<Seat>>(s => s.Count == 22));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateVenue_ByNonAdmin_ThrowsForbidden()
    {
        _currentUser.IsAdmin.Returns(false);

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            VenueHandler().HandleAsync(VenueCommand(), default));

        _venues.DidNotReceiveWithAnyArgs().Add(default!, default!);
    }

    [Fact]
    public async Task CreateVenue_Anonymous_ThrowsUnauthorized()
    {
        _currentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            VenueHandler().HandleAsync(VenueCommand(), default));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("TOOLONGROW", 10)]   // seats.row_label varchar(8)
    [InlineData("A", 0)]
    [InlineData("A", 201)]
    public async Task CreateVenue_WithInvalidRow_ThrowsValidation(string label, int count)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            VenueHandler().HandleAsync(VenueCommand(new VenueRowInput(label, count)), default));
    }

    [Fact]
    public async Task CreateVenue_ExceedingSeatLimit_ThrowsValidation()
    {
        var rows = Enumerable.Range(0, 30)
            .Select(i => new VenueRowInput($"R{i}", 200))   // 6000 koltuk
            .ToArray();

        await Assert.ThrowsAsync<ValidationException>(() =>
            VenueHandler().HandleAsync(VenueCommand(rows), default));
    }

    // ---------- CreateEvent ----------

    [Fact]
    public async Task CreateEvent_CreatesSeatForEveryVenueSeat_AndInvalidatesCache()
    {
        var venue = GivenVenueWithLayout(("A", 5), ("B", 5));

        var result = await EventHandler().HandleAsync(EventCommand(venue.Id), default);

        Assert.Equal(10, result.SeatCount);
        _events.Received(1).Add(Arg.Is<Event>(e =>
            e.Id == result.Id && e.EventSeats.Count == 10 && e.EventSeats.All(s => s.Price == 300m)));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _cache.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateEvent_AppliesRowPricesCaseInsensitively()
    {
        var venue = GivenVenueWithLayout(("A", 2), ("B", 2));
        Event? added = null;
        _events.Add(Arg.Do<Event>(e => added = e));

        await EventHandler().HandleAsync(
            EventCommand(venue.Id, [new RowPriceInput("a", 900m)]), default);

        Assert.NotNull(added);
        Assert.Equal([900m, 900m, 300m, 300m], added.EventSeats.Select(s => s.Price));
    }

    [Fact]
    public async Task CreateEvent_WithUnspecifiedKindDates_StoresUtc()
    {
        var venue = GivenVenueWithLayout(("A", 1));
        Event? added = null;
        _events.Add(Arg.Do<Event>(e => added = e));

        var command = EventCommand(venue.Id) with
        {
            EventDate = DateTime.SpecifyKind(Now.AddDays(30), DateTimeKind.Unspecified)
        };

        await EventHandler().HandleAsync(command, default);

        Assert.Equal(DateTimeKind.Utc, added!.EventDate.Kind);
    }

    [Fact]
    public async Task CreateEvent_UnknownVenue_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            EventHandler().HandleAsync(EventCommand(Guid.CreateVersion7()), default));

        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateEvent_ByNonAdmin_ThrowsForbidden()
    {
        _currentUser.IsAdmin.Returns(false);
        var venue = GivenVenueWithLayout(("A", 1));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            EventHandler().HandleAsync(EventCommand(venue.Id), default));
    }

    [Fact]
    public async Task CreateEvent_WithInvalidSalesWindow_ThrowsDomainException()
    {
        var venue = GivenVenueWithLayout(("A", 1));
        var command = EventCommand(venue.Id) with { SalesEndAt = Now.AddDays(31) };

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            EventHandler().HandleAsync(command, default));

        Assert.Equal("event.sales_after_event", ex.Code);
    }

    [Fact]
    public async Task CreateEvent_WithNonPositivePrice_ThrowsValidation()
    {
        var venue = GivenVenueWithLayout(("A", 1));

        await Assert.ThrowsAsync<ValidationException>(() =>
            EventHandler().HandleAsync(EventCommand(venue.Id) with { DefaultPrice = 0m }, default));
    }
}
