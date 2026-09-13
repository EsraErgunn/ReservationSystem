using NSubstitute;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations;
using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Application.Tests;

/// <summary>
/// FR-10 okuma tarafının yetki kuralları. Sorguların kendisi (LINQ projeksiyonu)
/// gerçek veritabanı ister — o entegrasyon testlerine bırakıldı
/// (feature-auth-plani.md §4.3).
/// </summary>
public class ReservationQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly IReservationQueries _queries = Substitute.For<IReservationQueries>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private static ReservationDetailDto Detail(Guid id) => new(
        Id: id,
        EventId: Guid.CreateVersion7(),
        EventTitle: "Konser",
        EventDate: Now.AddDays(30),
        VenueName: "Salon",
        Status: "Held",
        TotalAmount: 250m,
        HeldUntil: Now.AddMinutes(10),
        CreatedAt: Now,
        Seats: [new ReservationSeatDto(Guid.CreateVersion7(), "A", 1, 250m)],
        LastPaymentStatus: null);

    private GetReservationByIdHandler ByIdHandler() => new(_queries, _currentUser);

    private GetMyReservationsHandler MineHandler() => new(_queries, _currentUser);

    [Fact]
    public async Task GetById_OwnReservation_Returns()
    {
        var owner = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        _currentUser.UserId.Returns(owner);
        _queries.GetDetailAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ReservationDetailWithOwnerDto(owner, Detail(id)));

        var result = await ByIdHandler().HandleAsync(new GetReservationByIdQuery(id), default);

        Assert.Equal(id, result.Id);
    }

    [Fact]
    public async Task GetById_SomeoneElsesReservation_Throws()
    {
        var id = Guid.CreateVersion7();

        _currentUser.UserId.Returns(Guid.CreateVersion7());   // başkası
        _currentUser.IsAdmin.Returns(false);
        _queries.GetDetailAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ReservationDetailWithOwnerDto(Guid.CreateVersion7(), Detail(id)));

        await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            ByIdHandler().HandleAsync(new GetReservationByIdQuery(id), default));
    }

    [Fact]
    public async Task GetById_AdminCanSeeSomeoneElses()
    {
        var id = Guid.CreateVersion7();

        _currentUser.UserId.Returns(Guid.CreateVersion7());
        _currentUser.IsAdmin.Returns(true);
        _queries.GetDetailAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ReservationDetailWithOwnerDto(Guid.CreateVersion7(), Detail(id)));

        var result = await ByIdHandler().HandleAsync(new GetReservationByIdQuery(id), default);

        Assert.Equal(id, result.Id);
    }

    [Fact]
    public async Task GetById_UnknownId_Throws()
    {
        var id = Guid.CreateVersion7();

        _currentUser.UserId.Returns(Guid.CreateVersion7());
        _queries.GetDetailAsync(id, Arg.Any<CancellationToken>())
            .Returns((ReservationDetailWithOwnerDto?)null);

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            ByIdHandler().HandleAsync(new GetReservationByIdQuery(id), default));
    }

    [Fact]
    public async Task GetById_Anonymous_Throws()
    {
        _currentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            ByIdHandler().HandleAsync(
                new GetReservationByIdQuery(Guid.CreateVersion7()), default));
    }

    /// <summary>Kimliksiz çağrıda sorgu hiç çalıştırılmamalı.</summary>
    [Fact]
    public async Task GetById_Anonymous_DoesNotQuery()
    {
        _currentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            ByIdHandler().HandleAsync(
                new GetReservationByIdQuery(Guid.CreateVersion7()), default));

        await _queries.DidNotReceive().GetDetailAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMine_QueriesForCurrentUser()
    {
        var userId = Guid.CreateVersion7();

        _currentUser.UserId.Returns(userId);
        _queries.GetByUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await MineHandler().HandleAsync(new GetMyReservationsQuery(), default);

        Assert.Empty(result);
        await _queries.Received(1).GetByUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMine_Anonymous_Throws()
    {
        _currentUser.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            MineHandler().HandleAsync(new GetMyReservationsQuery(), default));
    }
}
