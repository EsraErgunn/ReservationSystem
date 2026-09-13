using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class ReservationRepository(AppDbContext context) : IReservationRepository
{
    public Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Reservations
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Reservation?> GetWithPaymentsAsync(Guid id, CancellationToken ct) =>
        context.Reservations
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Reservation?> GetByPaymentTokenAsync(string token, CancellationToken ct) =>
        context.Reservations
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Payments.Any(p => p.ProviderToken == token), ct);

    /// <summary>BR-02: süresi dolmuş hold'lar. ix_reservations_status_held_until kullanır.</summary>
    public async Task<IReadOnlyList<Reservation>> GetExpiredAsync(
        DateTime utcNow, int batchSize, CancellationToken ct) =>
        await context.Reservations
            .Include(r => r.Items)
            .Where(r => r.Status == ReservationStatus.Held && r.HeldUntil < utcNow)
            .OrderBy(r => r.HeldUntil)
            .Take(batchSize)
            .ToListAsync(ct);

    /// <summary>
    /// api-katmani.md §8. Sıralama <c>CreatedAt</c> ile: en uzun süredir bekleyen
    /// (yani en riskli) kayıt önce ele alınır.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPendingPaymentTokensAsync(
        DateTime olderThanUtc, int batchSize, CancellationToken ct) =>
        await context.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Pending
                        && p.ProviderToken != null
                        && p.CreatedAt < olderThanUtc)
            .OrderBy(p => p.CreatedAt)
            .Take(batchSize)
            .Select(p => p.ProviderToken!)
            .ToListAsync(ct);

    public void Add(Reservation reservation) => context.Reservations.Add(reservation);
}
