namespace ReservationSystem.Application.Reservations.Dtos;

/// <summary>
/// FR-10 liste görünümü. <see cref="HeldUntil"/> yalnızca <c>Held</c> durumunda
/// dolu: onaylanmış bir rezervasyonda geri sayım göstermek yanlış olur.
/// </summary>
public record ReservationSummaryDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTime EventDate,
    string Status,
    int SeatCount,
    decimal TotalAmount,
    DateTime? HeldUntil,
    DateTime CreatedAt);

/// <summary>
/// FR-10 detay görünümü. <c>UserId</c> bilinçli olarak dışa çıkmıyor — istemcinin
/// bilmesine gerek yok; yetki kontrolü için gereken sahip bilgisi
/// <see cref="ReservationDetailWithOwnerDto"/> ile taşınır.
/// </summary>
public record ReservationDetailDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTime EventDate,
    string VenueName,
    string Status,
    decimal TotalAmount,
    DateTime? HeldUntil,
    DateTime CreatedAt,
    IReadOnlyList<ReservationSeatDto> Seats,
    string? LastPaymentStatus);

public record ReservationSeatDto(
    Guid EventSeatId,
    string RowLabel,
    int SeatNumber,
    decimal Price);

/// <summary>
/// Okuma tarafının iç tipi: BR-15 kontrolü için sahibi taşır, dışarı verilmez.
/// </summary>
public record ReservationDetailWithOwnerDto(
    Guid UserId,
    ReservationDetailDto Detail);
