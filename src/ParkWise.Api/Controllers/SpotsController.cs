using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Parking;

namespace ParkWise.Api.Controllers;

[Route("api/v1")]
[Authorize]
public class SpotsController(ISpotService spots) : ApiControllerBase
{
    [HttpGet("lots/{lotId:guid}/spots")]
    public async Task<IActionResult> List(Guid lotId, [FromQuery] SpotListQuery query, CancellationToken ct)
        => ToActionResult(await spots.ListAsync(lotId, query, ct));

    [HttpGet("spots/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await spots.GetAsync(id, ct));

    [HttpPost("levels/{levelId:guid}/spots")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<SpotDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid levelId, CreateSpotRequest request, CancellationToken ct)
        => ToActionResult(await spots.CreateAsync(levelId, request, ct), dto => Created($"/api/v1/spots/{dto.Id}", dto));

    [HttpPut("spots/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, UpdateSpotRequest request, CancellationToken ct)
        => ToActionResult(await spots.UpdateAsync(id, request, ct));

    /// <summary>Takes a spot out of service (inService=false) or returns it (inService=true).</summary>
    [HttpPut("spots/{id:guid}/service")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetService(Guid id, SetSpotServiceRequest request, CancellationToken ct)
        => ToActionResult(await spots.SetServiceAsync(id, request.InService, ct));

    [HttpDelete("spots/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToActionResult(await spots.DeleteAsync(id, ct));
}
