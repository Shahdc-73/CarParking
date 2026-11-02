using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Vehicles;

namespace ParkWise.Api.Controllers;

[Route("api/v1/vehicles")]
[Authorize] // drivers see only their own vehicles; staff see all (enforced in VehicleService)
public class VehiclesController(IVehicleService vehicles) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<VehicleDto>>> List([FromQuery] VehicleListQuery query, CancellationToken ct)
        => Ok(await vehicles.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await vehicles.GetAsync(id, ct));

    /// <summary>Exact plate lookup for the gate screen.</summary>
    [HttpGet("lookup")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> Lookup([FromQuery] string plate, CancellationToken ct)
        => ToActionResult(await vehicles.LookupByPlateAsync(plate, ct));

    [HttpPost]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateVehicleRequest request, CancellationToken ct)
        => ToActionResult(await vehicles.CreateAsync(request, ct), dto => Created($"/api/v1/vehicles/{dto.Id}", dto));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateVehicleRequest request, CancellationToken ct)
        => ToActionResult(await vehicles.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToActionResult(await vehicles.DeleteAsync(id, ct));
}
