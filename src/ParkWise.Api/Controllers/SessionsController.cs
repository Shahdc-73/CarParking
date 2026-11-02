using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Sessions;

namespace ParkWise.Api.Controllers;

[Route("api/v1/sessions")]
[Authorize] // drivers see only sessions of their own vehicles (enforced in SessionService)
public class SessionsController(ISessionService sessions) : ApiControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    [HttpGet]
    public async Task<ActionResult<PagedResult<SessionDto>>> List([FromQuery] SessionListQuery query, CancellationToken ct)
        => Ok(await sessions.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await sessions.GetAsync(id, ct));

    [HttpGet("{id:guid}/fee-preview")]
    public async Task<IActionResult> FeePreview(Guid id, CancellationToken ct)
        => ToActionResult(await sessions.PreviewFeeAsync(id, ct));

    [HttpPost("check-in")]
    [Authorize(Roles = Roles.Staff)]
    [ProducesResponseType<SessionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CheckIn(CheckInRequest request, CancellationToken ct)
        => ToActionResult(await sessions.CheckInAsync(request, ct), dto => Created($"/api/v1/sessions/{dto.Id}", dto));

    /// <summary>
    /// Idempotent: calling it again for an already closed session returns the same receipt (header
    /// <c>Idempotent-Replayed: true</c>) and never charges twice. An optional <c>Idempotency-Key</c> header is stored with the payment.
    /// </summary>
    [HttpPost("{id:guid}/check-out")]
    [Authorize(Roles = Roles.Staff)]
    [ProducesResponseType<ReceiptDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckOut(Guid id, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey, CancellationToken ct)
        => ToCheckOutResult(await sessions.CheckOutAsync(id, idempotencyKey, ct));

    /// <summary>Check-out for a driver who lost their ticket: a flat fee replaces the normal fee.</summary>
    [HttpPost("lost-ticket")]
    [Authorize(Roles = Roles.Staff)]
    [ProducesResponseType<ReceiptDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LostTicket(LostTicketRequest request, CancellationToken ct)
        => ToCheckOutResult(await sessions.LostTicketAsync(request, ct));

    private IActionResult ToCheckOutResult(Result<CheckOutOutcome> result)
    {
        if (!result.IsSuccess) return FromError(result.Error!);

        Response.Headers[ReplayedHeader] = result.Value.Replayed ? "true" : "false";
        return Ok(result.Value.Receipt);
    }
}
