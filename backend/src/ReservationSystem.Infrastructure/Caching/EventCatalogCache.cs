using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ReservationSystem.Application.Abstractions;

namespace ReservationSystem.Infrastructure.Caching;

/// <summary>
/// NFR-02. <see cref="IDistributedCache"/> Redis bağlantı dizesi verildiğinde Redis,
/// verilmediğinde bellek içi implementasyondur — bu sınıf farkı bilmez.
/// </summary>
public class EventCatalogCache(
    IDistributedCache cache,
    ILogger<EventCatalogCache> logger) : IEventCatalogCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public static string ListKey(bool includePast) => $"events:list:{(includePast ? "all" : "upcoming")}";

    public async Task InvalidateAsync(CancellationToken ct)
    {
        try
        {
            await cache.RemoveAsync(ListKey(false), ct);
            await cache.RemoveAsync(ListKey(true), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // En kötü durumda liste TTL kadar bayat kalır — işlemi geri almaya değmez.
            logger.LogWarning(ex, "Etkinlik cache'i temizlenemedi.");
        }
    }
}
