using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ReservationSystem.Application.Abstractions;

namespace ReservationSystem.Infrastructure.RealTime;

public class SignalRSeatNotifier(
    IHubContext<SeatHub> hub,
    ILogger<SignalRSeatNotifier> logger) : ISeatAvailabilityNotifier
{
    public async Task NotifySeatsChangedAsync(
        Guid eventId, IReadOnlyList<Guid> seatIds, CancellationToken ct)
    {
        try
        {
            await hub.Clients
                .Group(SeatHub.GroupFor(eventId))
                .SendAsync(SeatHub.SeatsChanged, new { eventId, seatIds }, ct);
        }
        catch (Exception ex)
        {
            // Bildirim en iyi çaba: veritabanı zaten commit edildi, bildirimin
            // başarısızlığı işlemi geri almamalı. Gerçek çözüm outbox pattern
            // (application-katmani.md §5/C'de kabul edilen eksiklik).
            logger.LogWarning(ex,
                "Koltuk değişikliği yayınlanamadı. Etkinlik={EventId} KoltukSayisi={Count}",
                eventId, seatIds.Count);
        }
    }
}
