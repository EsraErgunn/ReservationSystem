using Microsoft.Extensions.Options;
using NSubstitute;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Payments;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Tests;

/// <summary>
/// Handler testleri için ortak kurulum: gerçek domain nesneleri + mock portlar.
/// Domain testlerinin aksine burada mock kullanılır (application-katmani.md §12).
/// </summary>
public class TestScenario
{
    public static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    public IEventRepository Events { get; } = Substitute.For<IEventRepository>();
    public IEventSeatRepository Seats { get; } = Substitute.For<IEventSeatRepository>();
    public IReservationRepository Reservations { get; } = Substitute.For<IReservationRepository>();
    public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
    public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public ISeatAvailabilityNotifier Notifier { get; } = Substitute.For<ISeatAvailabilityNotifier>();
    public IPaymentGateway Gateway { get; } = Substitute.For<IPaymentGateway>();

    public FixedClock Clock { get; } = new(Now);

    public IOptions<PaymentOptions> PaymentOptions { get; } =
        Options.Create(new PaymentOptions { CallbackUrl = "https://localhost/payments/callback" });

    public Event Event { get; }
    public EventSeat[] EventSeats { get; }
    public User Buyer { get; }

    public TestScenario(int seatCount = 1)
    {
        Event = new Event(
            venueId: Guid.CreateVersion7(),
            title: "Konser",
            eventDate: Now.AddDays(30),
            salesStartAt: Now.AddDays(-1),
            salesEndAt: Now.AddDays(20));

        EventSeats = Enumerable
            .Range(0, seatCount)
            .Select(i => new EventSeat(Event.Id, Guid.CreateVersion7(), 250m + i))
            .ToArray();

        Buyer = new User("esra@example.com", "hash", "Esra E.", Now);
        CurrentUser.UserId.Returns(Buyer.Id);
        CurrentUser.IsAdmin.Returns(false);

        Events.GetByIdAsync(Event.Id, Arg.Any<CancellationToken>()).Returns(Event);
        Users.GetByIdAsync(Buyer.Id, Arg.Any<CancellationToken>()).Returns(Buyer);
        Seats.GetByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(EventSeats);

        // ExecuteInTransactionAsync varsayılan olarak delegeyi gerçekten çalıştırsın.
        Uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));
    }

    /// <summary>Held durumda bir rezervasyon kurar ve repository'lere bağlar.</summary>
    public Reservation GivenHeldReservation()
    {
        var reservation = Reservation.Create(Buyer.Id, Event, EventSeats, Now);

        Reservations.GetByIdAsync(reservation.Id, Arg.Any<CancellationToken>()).Returns(reservation);
        Reservations.GetWithPaymentsAsync(reservation.Id, Arg.Any<CancellationToken>()).Returns(reservation);
        Seats.GetByReservationAsync(reservation.Id, Arg.Any<CancellationToken>()).Returns(EventSeats);

        return reservation;
    }

    /// <summary>Held rezervasyon + sağlayıcı token'ı iliştirilmiş Pending ödeme.</summary>
    public (Reservation Reservation, Payment Payment) GivenPendingPayment(string token = "tok_123")
    {
        var reservation = GivenHeldReservation();
        var payment = reservation.StartPayment(Now);
        payment.AttachProviderToken(token);

        Reservations.GetByPaymentTokenAsync(token, Arg.Any<CancellationToken>()).Returns(reservation);

        return (reservation, payment);
    }
}

/// <summary>
/// Sabit zamanlı <see cref="TimeProvider"/>. Microsoft.Extensions.TimeProvider.Testing
/// paketindeki FakeTimeProvider'ın ihtiyacımız olan kadarı — ek bağımlılık getirmiyor.
/// </summary>
public class FixedClock(DateTime utcNow) : TimeProvider
{
    private DateTimeOffset _now = new(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
