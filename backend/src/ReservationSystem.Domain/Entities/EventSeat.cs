using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Entities;

/// <summary>
/// Bir etkinlikteki belirli bir koltuğun satılabilir hâli. Müsaitlik ve fiyat
/// burada tutulur; concurrency kontrolü (xmin) Infrastructure katmanında shadow
/// property olarak eklenir — Domain'in PostgreSQL'den haberi olmaz.
/// </summary>
public class EventSeat : Entity
{
    public Guid EventId { get; private set; }
    public Guid SeatId { get; private set; }
    public decimal Price { get; private set; }
    public SeatStatus Status { get; private set; }

    public Event Event { get; private set; } = null!;
    public Seat Seat { get; private set; } = null!;

    private EventSeat() { }   // EF Core için

    public EventSeat(Guid eventId, Guid seatId, decimal price)
    {
        if (price < 0)
            throw new DomainException("seat.invalid_price", "Fiyat negatif olamaz.");

        EventId = eventId;
        SeatId = seatId;
        Price = price;
        Status = SeatStatus.Available;
    }

    public void Hold()
    {
        if (Status != SeatStatus.Available)
            throw new DomainException("seat.not_available", "Koltuk müsait değil.");

        Status = SeatStatus.Held;
    }

    public void Release()
    {
        if (Status == SeatStatus.Sold)
            throw new DomainException("seat.already_sold", "Satılmış koltuk serbest bırakılamaz.");

        Status = SeatStatus.Available;
    }

    public void MarkSold()
    {
        if (Status != SeatStatus.Held)
            throw new DomainException("seat.not_held", "Yalnızca hold edilmiş koltuk satılabilir.");

        Status = SeatStatus.Sold;
    }
}
