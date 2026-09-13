using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;

namespace ReservationSystem.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(
    IQueryHandler<GetSeatMapQuery, SeatMapDto> seatMap)
    : ControllerBase
{
    /// <summary>FR-03: koltuk haritası. Ziyaretçi de görebilir.</summary>
    /// <remarks>
    /// 5 saniyelik cache — koltuk durumu hızlı değişiyor, uzunu yanlış bilgi gösterir.
    /// Asıl güncellik SignalR'dan geliyor; bu yalnızca ilk açılış yükünü azaltıyor.
    /// </remarks>
    [HttpGet("{id:guid}/seats")]
    [AllowAnonymous]
    [ResponseCache(Duration = 5)]
    [ProducesResponseType<SeatMapDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatMap(Guid id, CancellationToken ct)
        => Ok(await seatMap.HandleAsync(new GetSeatMapQuery(id), ct));
}
