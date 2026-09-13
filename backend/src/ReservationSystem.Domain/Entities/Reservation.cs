using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;
using ReservationSystem.Domain.Rules;

namespace ReservationSystem.Domain.Entities;

/// <summary>
/// Aggregate root. <see cref="EventSeat"/> durumu bilinçli olarak aynı transaction
/// içinde güncellenir (bkz. domain-katmani.md §8.1): koltuk satışında eventual
/// consistency, aynı koltuğun iki kez satılması riskini doğurur.
/// </summary>
public class Reservation : Entity
{
    private readonly List<ReservationItem> _items = [];
    private readonly List<Payment> _payments = [];

    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime HeldUntil { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<ReservationItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    private Reservation() { }   // EF Core için

    public static Reservation Create(
        Guid userId,
        Event @event,
        IReadOnlyList<EventSeat> seats,
        DateTime utcNow)
    {
        if (seats.Count == 0)
            throw new DomainException("reservation.no_seats", "En az bir koltuk seçilmelidir.");

        // BR-03
        if (seats.Count > ReservationRules.MaxSeatsPerReservation)
            throw new DomainException(
                "reservation.too_many_seats",
                $"Tek rezervasyonda en fazla {ReservationRules.MaxSeatsPerReservation} koltuk seçilebilir.");

        // BR-17
        if (!@event.IsOnSale(utcNow))
            throw new DomainException("reservation.sales_closed", "Bilet satışı bu etkinlik için kapalı.");

        if (seats.Any(s => s.EventId != @event.Id))
            throw new DomainException("reservation.seat_event_mismatch", "Koltuklar bu etkinliğe ait değil.");

        if (seats.Select(s => s.Id).Distinct().Count() != seats.Count)
            throw new DomainException("reservation.duplicate_seat", "Aynı koltuk birden fazla kez seçilemez.");

        var reservation = new Reservation
        {
            UserId = userId,
            EventId = @event.Id,
            Status = ReservationStatus.Held,
            HeldUntil = utcNow.Add(ReservationRules.HoldDuration),   // BR-01
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        foreach (var seat in seats)
        {
            seat.Hold();
            reservation._items.Add(new ReservationItem(reservation.Id, seat.Id, seat.Price));
        }

        reservation.TotalAmount = reservation._items.Sum(i => i.PriceAtReservation);

        return reservation;
    }

    public bool IsExpired(DateTime utcNow) =>
        Status == ReservationStatus.Held && utcNow > HeldUntil;

    /// <summary>BR-10: ödeme sunucu tarafında doğrulandıktan sonra çağrılır.</summary>
    public void Confirm(IReadOnlyList<EventSeat> seats, DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        // Hold süresi dolarken ödeme yarışı
        if (utcNow > HeldUntil)
            throw new DomainException(
                "reservation.expired",
                "Rezervasyon süresi doldu; ödeme iade edilmelidir.");

        foreach (var seat in MatchSeats(seats))
            seat.MarkSold();

        Status = ReservationStatus.Confirmed;
        UpdatedAt = utcNow;
    }

    /// <summary>BR-02: hold süresi dolduğunda arka plan görevi tarafından çağrılır.</summary>
    public void Expire(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Expired, seats, utcNow);

    public void Cancel(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Cancelled, seats, utcNow);

    /// <summary>BR-11: ödeme başarısız olduğunda hold serbest bırakılır.</summary>
    public void MarkFailed(IReadOnlyList<EventSeat> seats, DateTime utcNow)
        => Terminate(ReservationStatus.Failed, seats, utcNow);

    private void Terminate(
        ReservationStatus newStatus,
        IReadOnlyList<EventSeat> seats,
        DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        foreach (var seat in MatchSeats(seats))
            seat.Release();

        foreach (var item in _items)
            item.Deactivate();

        Status = newStatus;
        UpdatedAt = utcNow;
    }

    public Payment StartPayment(DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Held);

        if (utcNow > HeldUntil)
            throw new DomainException("reservation.expired", "Rezervasyon süresi doldu.");

        // Aynı anda tek Pending ödeme
        var pending = _payments.FirstOrDefault(p => p.Status == PaymentStatus.Pending);
        pending?.Abandon(utcNow);

        var payment = new Payment(Id, TotalAmount, utcNow);
        _payments.Add(payment);
        return payment;
    }

    private void EnsureStatus(ReservationStatus expected)
    {
        if (Status != expected)
            throw new DomainException(
                "reservation.invalid_state",
                $"Bu işlem için rezervasyon durumu '{expected}' olmalı, mevcut durum '{Status}'.");
    }

    /// <summary>Verilen koltuk listesinin bu rezervasyonun satırlarıyla tam eşleştiğini doğrular.</summary>
    private IEnumerable<EventSeat> MatchSeats(IReadOnlyList<EventSeat> seats)
    {
        var required = _items.Select(i => i.EventSeatId).ToHashSet();
        var provided = seats.Select(s => s.Id).ToHashSet();

        if (!required.SetEquals(provided))
            throw new DomainException(
                "reservation.seat_mismatch",
                "Verilen koltuklar rezervasyon satırlarıyla eşleşmiyor.");

        return seats;
    }
}
