using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Reservations.Dtos;

public record ReservationDto(
    Guid Id,
    Guid EventId,
    string Status,
    DateTime HeldUntil,
    decimal TotalAmount,
    IReadOnlyList<ReservationItemDto> Items)
{
    public static ReservationDto From(Reservation reservation) => new(
        reservation.Id,
        reservation.EventId,
        reservation.Status.ToString(),
        reservation.HeldUntil,
        reservation.TotalAmount,
        reservation.Items
            .Select(i => new ReservationItemDto(i.EventSeatId, i.PriceAtReservation))
            .ToList());
}

public record ReservationItemDto(Guid EventSeatId, decimal Price);
