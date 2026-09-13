using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Events;

/// <summary>
/// FR-03. Handler'ı Infrastructure'da: AsNoTracking() + projeksiyon.
/// Domain entity'leri materyalize edilmez (NFR-01 p95 hedefi).
/// </summary>
public record GetSeatMapQuery(Guid EventId) : IQuery<SeatMapDto>;

public record SeatMapDto(
    Guid EventId,
    string EventTitle,
    IReadOnlyList<SeatMapItemDto> Seats);

public record SeatMapItemDto(
    Guid EventSeatId,
    string RowLabel,
    int SeatNumber,
    decimal Price,
    string Status);
