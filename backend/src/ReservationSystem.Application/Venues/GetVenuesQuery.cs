using ReservationSystem.Application.Common;

namespace ReservationSystem.Application.Venues;

/// <summary>FR-12: admin etkinlik tanımlarken mekân seçer. Handler Infrastructure'da.</summary>
public record GetVenuesQuery : IQuery<IReadOnlyList<VenueDto>>;
