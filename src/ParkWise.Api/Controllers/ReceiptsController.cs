using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Sessions;

namespace ParkWise.Api.Controllers;

[Route("api/v1/receipts")]
[Authorize]
public class ReceiptsController(ISessionService sessions) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await sessions.GetReceiptAsync(id, ct));
}
