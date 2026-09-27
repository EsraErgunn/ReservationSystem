using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReservationSystem.Application.Common;
using ReservationSystem.Application.Venues;

namespace ReservationSystem.Api.Controllers;

/// <summary>FR-12 / BR-16: mekân yönetimi yalnızca admin rolüne açık.</summary>
[ApiController]
[Route("api/venues")]
[Authorize(Policy = AuthPolicies.Admin)]
public class VenuesController(
    IQueryHandler<GetVenuesQuery, IReadOnlyList<VenueDto>> list,
    ICommandHandler<CreateVenueCommand, VenueDto> create)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VenueDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await list.HandleAsync(new GetVenuesQuery(), ct));

    [HttpPost]
    [ProducesResponseType<VenueDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateVenueCommand command, CancellationToken ct)
    {
        var result = await create.HandleAsync(command, ct);
        return Created($"/api/venues/{result.Id}", result);
    }
}
