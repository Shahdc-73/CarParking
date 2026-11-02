using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkWise.Api.Infrastructure;
using ParkWise.Application.Auth;
using ParkWise.Application.Pricing;
using ParkWise.Domain.Enums;

namespace ParkWise.Api.Controllers;

[Route("api/v1/pricing")]
[Authorize] // everyone may see the prices; only admins change them
public class PricingController(IPricingService pricing) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PricingOverviewDto>> Get(CancellationToken ct)
        => Ok(await pricing.GetCurrentAsync(ct));

    /// <summary>Adds a NEW price version for a spot type. Existing versions (and old receipts) are never edited.</summary>
    [HttpPut("{spotType}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetRule(SpotType spotType, SavePricingRuleRequest request, CancellationToken ct)
        => ToActionResult(await pricing.SetRuleAsync(spotType, request, ct));

    [HttpPut("lost-ticket-fee")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetLostTicketFee(SaveLostTicketFeeRequest request, CancellationToken ct)
        => ToActionResult(await pricing.SetLostTicketFeeAsync(request, ct));
}
