using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Audit;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;

namespace ParkWise.Api.Controllers;

[Route("api/v1/audit-logs")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController(IAuditQueryService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List([FromQuery] AuditListQuery query, CancellationToken ct)
        => Ok(await audit.ListAsync(query, ct));
}
