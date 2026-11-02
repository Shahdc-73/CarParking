using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Reports;

namespace ParkWise.Api.Controllers;

[Route("api/v1/reports")]
[Authorize(Roles = Roles.Admin)]
public class ReportsController(IReportService reports) : ApiControllerBase
{
    /// <summary>Occupancy of every lot right now (and overall). The dashboard refreshes this live.</summary>
    [HttpGet("occupancy")]
    public async Task<ActionResult<OccupancyReportDto>> Occupancy(CancellationToken ct)
        => Ok(await reports.GetOccupancyAsync(ct));

    /// <summary>Revenue per day, average stay and peak hours for a range of local days (max 92), optionally for one lot.</summary>
    [HttpGet("performance")]
    public async Task<IActionResult> Performance([FromQuery] PerformanceQuery query, CancellationToken ct)
        => ToActionResult(await reports.GetPerformanceAsync(query, ct));
}
