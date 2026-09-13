using ReservationSystem.Domain.Common;

namespace ReservationSystem.Domain.Entities;

/// <summary>
/// Mekâna ait fiziksel koltuk. Etkinlikten bağımsızdır; müsaitlik burada değil
/// <see cref="EventSeat"/> üzerinde tutulur.
/// </summary>
public class Seat : Entity
{
    public Guid VenueId { get; private set; }
    public string RowLabel { get; private set; } = null!;
    public int SeatNumber { get; private set; }

    private Seat() { }   // EF Core için

    public Seat(Guid venueId, string rowLabel, int seatNumber)
    {
        if (seatNumber <= 0)
            throw new DomainException("seat.invalid_number", "Koltuk numarası pozitif olmalı.");

        VenueId = venueId;
        RowLabel = rowLabel;
        SeatNumber = seatNumber;
    }

    public string Label => $"{RowLabel}-{SeatNumber}";
}
