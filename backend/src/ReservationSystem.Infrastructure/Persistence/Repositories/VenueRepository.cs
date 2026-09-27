using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class VenueRepository(AppDbContext context) : IVenueRepository
{
    public Task<Venue?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Venues.FirstOrDefaultAsync(v => v.Id == id, ct);

    /// <summary>
    /// Takip edilmeden okunur: koltuklar yalnızca EventSeat üretmek için kimlik ve
    /// sıra bilgisi sağlıyor, değiştirilmiyor.
    /// </summary>
    public async Task<IReadOnlyList<Seat>> GetSeatsAsync(Guid venueId, CancellationToken ct) =>
        await context.Seats
            .AsNoTracking()
            .Where(s => s.VenueId == venueId)
            .OrderBy(s => s.RowLabel)
            .ThenBy(s => s.SeatNumber)
            .ToListAsync(ct);

    public void Add(Venue venue, IReadOnlyList<Seat> seats)
    {
        context.Venues.Add(venue);
        context.Seats.AddRange(seats);
    }
}
