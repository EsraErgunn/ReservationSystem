using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Infrastructure.Persistence;

/// <summary>
/// İstisna çevirim noktası (application-katmani.md §5/A, seçilen 1. seçenek):
/// EF Core ve Npgsql tipleri burada Application'ın tanıdığı tiplere dönüşür,
/// böylece bu isim alanları Infrastructure'da kalır.
/// </summary>
public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private const string UniqueViolation = "23505";

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex.Message, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation } pg)
        {
            throw new UniqueConstraintException(pg.ConstraintName, ex);
        }
    }

    /// <summary>
    /// F maddesi: Reservation + Items + EventSeats aynı transaction'da.
    /// Zaten bir transaction açıksa (iç içe çağrı) yenisini açmaz.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null)
            return await operation(ct);

        // Geçici bağlantı hataları için EF Core'un execution strategy'si —
        // concurrency çakışmasını DEĞİL, yalnızca bağlantı kopmasını retry eder.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
