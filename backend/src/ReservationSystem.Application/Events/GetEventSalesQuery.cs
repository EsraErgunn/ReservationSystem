using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Events;

/// <summary>FR-13: admin bir etkinliğin satış durumunu görür. Handler Infrastructure'da.</summary>
public record GetEventSalesQuery(Guid EventId) : IQuery<EventSalesDto>;

public record EventSalesDto(
    Guid EventId,
    string Title,
    DateTime EventDate,
    int TotalSeats,
    int AvailableSeats,
    int HeldSeats,
    int SoldSeats,
    decimal ConfirmedRevenue,
    IReadOnlyDictionary<string, int> ReservationsByStatus);
