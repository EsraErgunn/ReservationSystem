using ReservationSystem.Domain.Common;

namespace ReservationSystem.Domain.Entities;

public class Event : Entity
{
    private readonly List<EventSeat> _eventSeats = [];

    public Guid VenueId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTime EventDate { get; private set; }
    public DateTime SalesStartAt { get; private set; }
    public DateTime SalesEndAt { get; private set; }

    public IReadOnlyCollection<EventSeat> EventSeats => _eventSeats.AsReadOnly();

    private Event() { }   // EF Core için

    public Event(Guid venueId, string title, DateTime eventDate,
                 DateTime salesStartAt, DateTime salesEndAt, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("event.invalid_title", "Etkinlik başlığı boş olamaz.");

        if (salesStartAt >= salesEndAt)
            throw new DomainException("event.invalid_sales_window",
                "Satış başlangıcı bitişten önce olmalı.");

        if (salesEndAt > eventDate)
            throw new DomainException("event.sales_after_event",
                "Satış, etkinlik tarihinden sonra bitemez.");

        VenueId = venueId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        EventDate = eventDate;
        SalesStartAt = salesStartAt;
        SalesEndAt = salesEndAt;
    }

    /// <summary>
    /// FR-12: etkinliğin satılabilir koltuklarını üretir. Mekânın her koltuğu için
    /// tek bir <see cref="EventSeat"/> — fiyat sıra bazında değişebilir.
    /// </summary>
    public IReadOnlyList<EventSeat> CreateSeats(
        IReadOnlyList<Seat> venueSeats, Func<Seat, decimal> priceFor)
    {
        if (venueSeats.Count == 0)
            throw new DomainException("event.no_seats", "Mekânda tanımlı koltuk yok.");

        if (venueSeats.Any(s => s.VenueId != VenueId))
            throw new DomainException("event.seat_venue_mismatch", "Koltuklar etkinliğin mekânına ait değil.");

        if (_eventSeats.Count > 0)
            throw new DomainException("event.seats_already_created", "Etkinlik koltukları zaten oluşturuldu.");

        foreach (var seat in venueSeats)
            _eventSeats.Add(new EventSeat(Id, seat.Id, priceFor(seat)));

        return _eventSeats.AsReadOnly();
    }

    /// <summary>BR-17</summary>
    public bool IsOnSale(DateTime utcNow) =>
        utcNow >= SalesStartAt && utcNow <= SalesEndAt;
}
