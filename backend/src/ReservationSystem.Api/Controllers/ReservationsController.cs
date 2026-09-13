using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Reservations;
using ReservationSystem.Application.Reservations.Dtos;

namespace ReservationSystem.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize]                        // BR-14
public class ReservationsController(
    ICommandHandler<CreateReservationCommand, ReservationDto> create,
    ICommandHandler<CancelReservationCommand, bool> cancel,
    IQueryHandler<GetMyReservationsQuery, IReadOnlyList<ReservationSummaryDto>> getMine,
    IQueryHandler<GetReservationByIdQuery, ReservationDetailDto> getById)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("reservation")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateReservationRequest request, CancellationToken ct)
    {
        var result = await create.HandleAsync(
            new CreateReservationCommand(request.EventId, request.EventSeatIds), ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>FR-10: kullanıcının kendi rezervasyonları.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReservationSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
        => Ok(await getMine.HandleAsync(new GetMyReservationsQuery(), ct));

    /// <summary>FR-10: tek rezervasyonun detayı. Yetki kontrolü handler'da (BR-15).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReservationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await getById.HandleAsync(new GetReservationByIdQuery(id), ct));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await cancel.HandleAsync(new CancelReservationCommand(id), ct);
        return NoContent();
    }
}

public record CreateReservationRequest(Guid EventId, IReadOnlyList<Guid> EventSeatIds);
