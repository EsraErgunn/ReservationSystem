using ReservationSystem.Domain.Common;

namespace ReservationSystem.Domain.Entities;

/// <summary>
/// Yalnızca <see cref="Reservation"/> tarafından oluşturulup değiştirilebilir —
/// ctor ve mutasyon metodu bu yüzden <c>internal</c>.
/// </summary>
public class ReservationItem : Entity
{
    public Guid ReservationId { get; private set; }
    public Guid EventSeatId { get; private set; }
    public decimal PriceAtReservation { get; private set; }
    public bool IsActive { get; private set; }

    private ReservationItem() { }   // EF Core için

    internal ReservationItem(Guid reservationId, Guid eventSeatId, decimal price)
    {
        ReservationId = reservationId;
        EventSeatId = eventSeatId;
        PriceAtReservation = price;
        IsActive = true;
    }

    internal void Deactivate() => IsActive = false;
}
