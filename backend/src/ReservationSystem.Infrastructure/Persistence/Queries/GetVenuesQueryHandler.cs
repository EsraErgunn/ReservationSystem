using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Venues;

namespace ReservationSystem.Infrastructure.Persistence.Queries;

public class GetVenuesQueryHandler(AppDbContext context)
    : IQueryHandler<GetVenuesQuery, IReadOnlyList<VenueDto>>
{
    public async Task<IReadOnlyList<VenueDto>> HandleAsync(GetVenuesQuery query, CancellationToken ct) =>
        await context.Venues
            .AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new VenueDto(
                v.Id,
                v.Name,
                v.Address,
                v.City,
                context.Seats.Count(s => s.VenueId == v.Id)))
            .ToListAsync(ct);
}
