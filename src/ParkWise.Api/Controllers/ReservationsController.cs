using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Common;
using ParkWise.Application.Reservations;

namespace ParkWise.Api.Controllers;

[Route("api/v1/reservations")]
[Authorize] // drivers book for their own vehicles and see their own bookings; staff can do both for anyone (enforced in the service)
public class ReservationsController(IReservationService reservations) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationDto>>> List([FromQuery] ReservationListQuery query, CancellationToken ct)
        => Ok(await reservations.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await reservations.GetAsync(id, ct));

    /// <summary>How many spots of a type could still be reserved for a time window.</summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability([FromQuery] ReservationAvailabilityQuery query, CancellationToken ct)
        => ToActionResult(await reservations.GetAvailabilityAsync(query, ct));

    [HttpPost]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReservationRequest request, CancellationToken ct)
        => ToActionResult(await reservations.CreateAsync(request, ct), dto => Created($"/api/v1/reservations/{dto.Id}", dto));

    /// <summary>Cancels a pending reservation (the record is kept, with status Cancelled).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => ToActionResult(await reservations.CancelAsync(id, ct));
}
