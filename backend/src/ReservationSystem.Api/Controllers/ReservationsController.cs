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
    ICommandHandler<CancelReservationCommand, bool> cancel)
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

        // Dokümanda CreatedAtAction(nameof(GetById), ...) yazıyor; GetById action'ı
        // (ve karşılığı olan query) henüz yok, var olmayan action'a çözümlenemeyeceği
        // için Location başlığı elle kuruluyor.
        return Created($"/api/reservations/{result.Id}", result);
    }

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
