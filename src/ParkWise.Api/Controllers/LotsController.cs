using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;

namespace ParkWise.Api.Controllers;

[Route("api/v1")]
[Authorize]
public class LotsController(ILotService lots) : ApiControllerBase
{
    // ---- Lots ----
    [HttpGet("lots")]
    public async Task<ActionResult<PagedResult<LotDto>>> List([FromQuery] LotListQuery query, CancellationToken ct)
        => Ok(await lots.ListAsync(query, ct));

    [HttpGet("lots/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await lots.GetAsync(id, ct));

    [HttpPost("lots")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<LotDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveLotRequest request, CancellationToken ct)
        => ToActionResult(await lots.CreateAsync(request, ct), dto => Created($"/api/v1/lots/{dto.Id}", dto));

    [HttpPut("lots/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(Guid id, SaveLotRequest request, CancellationToken ct)
        => ToActionResult(await lots.UpdateAsync(id, request, ct));

    [HttpPut("lots/{id:guid}/active")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, SetActiveRequest request, CancellationToken ct)
        => ToActionResult(await lots.SetActiveAsync(id, request.IsActive, ct));

    [HttpDelete("lots/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToActionResult(await lots.DeleteAsync(id, ct));

    [HttpGet("lots/{id:guid}/availability")]
    public async Task<IActionResult> Availability(Guid id, CancellationToken ct)
        => ToActionResult(await lots.GetAvailabilityAsync(id, ct));

    // ---- Levels ----
    [HttpPost("lots/{lotId:guid}/levels")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<LevelDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddLevel(Guid lotId, CreateLevelRequest request, CancellationToken ct)
        => ToActionResult(await lots.AddLevelAsync(lotId, request, ct), dto => Created($"/api/v1/lots/{lotId}", dto));

    [HttpPut("levels/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateLevel(Guid id, UpdateLevelRequest request, CancellationToken ct)
        => ToActionResult(await lots.UpdateLevelAsync(id, request, ct));

    [HttpDelete("levels/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteLevel(Guid id, CancellationToken ct)
        => ToActionResult(await lots.DeleteLevelAsync(id, ct));
}
