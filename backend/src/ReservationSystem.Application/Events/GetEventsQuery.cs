using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Events;

/// <summary>
/// FR-02. Handler Infrastructure'da (projeksiyon + Redis cache, NFR-02).
/// Varsayılan olarak yalnızca tarihi geçmemiş etkinlikler döner.
/// </summary>
public record GetEventsQuery(bool IncludePast = false) : IQuery<IReadOnlyList<EventSummaryDto>>;

public record EventSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime EventDate,
    DateTime SalesStartAt,
    DateTime SalesEndAt,
    Guid VenueId,
    string VenueName,
    string City,
    decimal MinPrice,
    decimal MaxPrice,
    int AvailableSeats,
    int TotalSeats);

/// <summary>FR-02: etkinlik detayı.</summary>
public record GetEventByIdQuery(Guid Id) : IQuery<EventDetailDto>;

public record EventDetailDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime EventDate,
    DateTime SalesStartAt,
    DateTime SalesEndAt,
    Guid VenueId,
    string VenueName,
    string VenueAddress,
    string City,
    decimal MinPrice,
    decimal MaxPrice,
    int AvailableSeats,
    int TotalSeats);
