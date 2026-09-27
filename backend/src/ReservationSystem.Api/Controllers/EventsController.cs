using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Events;

namespace ReservationSystem.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(
    IQueryHandler<GetSeatMapQuery, SeatMapDto> seatMap,
    IQueryHandler<GetEventsQuery, IReadOnlyList<EventSummaryDto>> list,
    IQueryHandler<GetEventByIdQuery, EventDetailDto> detail,
    IQueryHandler<GetEventSalesQuery, EventSalesDto> sales,
    ICommandHandler<CreateEventCommand, EventCreatedDto> create)
    : ControllerBase
{
    /// <summary>FR-02: etkinlik listesi. Ziyaretçi de görebilir; sonuç Redis'te cache'li (NFR-02).</summary>
    /// <param name="includePast">Tarihi geçmiş etkinlikler de dahil edilsin mi (admin paneli).</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<EventSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool includePast, CancellationToken ct)
        => Ok(await list.HandleAsync(new GetEventsQuery(includePast), ct));

    /// <summary>FR-02: etkinlik detayı.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<EventDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await detail.HandleAsync(new GetEventByIdQuery(id), ct));

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

    /// <summary>FR-12: etkinlik tanımlama (BR-16: yalnızca admin).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<EventCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(CreateEventCommand command, CancellationToken ct)
    {
        var result = await create.HandleAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>FR-13: etkinliğin satış durumu (yalnızca admin).</summary>
    [HttpGet("{id:guid}/sales")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<EventSalesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSales(Guid id, CancellationToken ct)
        => Ok(await sales.HandleAsync(new GetEventSalesQuery(id), ct));
}
