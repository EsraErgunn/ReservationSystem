using Microsoft.AspNetCore.SignalR;

namespace ReservationSystem.Infrastructure.RealTime;

/// <summary>
/// FR-09. İstemci bir etkinliğin koltuk haritasını açtığında o etkinliğin
/// grubuna katılır; yayın yalnızca ilgili gruba gider.
/// </summary>
public class SeatHub : Hub
{
    public const string SeatsChanged = "SeatsChanged";

    public static string GroupFor(Guid eventId) => $"event:{eventId}";

    public Task JoinEvent(Guid eventId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(eventId));

    public Task LeaveEvent(Guid eventId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(eventId));
}
