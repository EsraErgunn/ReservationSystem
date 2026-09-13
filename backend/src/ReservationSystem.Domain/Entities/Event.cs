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
                 DateTime salesStartAt, DateTime salesEndAt)
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
        Title = title;
        EventDate = eventDate;
        SalesStartAt = salesStartAt;
        SalesEndAt = salesEndAt;
    }

    /// <summary>BR-17</summary>
    public bool IsOnSale(DateTime utcNow) =>
        utcNow >= SalesStartAt && utcNow <= SalesEndAt;
}
