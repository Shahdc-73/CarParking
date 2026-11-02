using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Pricing;

public sealed class PricingService(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : IPricingService
{
    // Tolerates a client clock that is a little behind, so "now" from a browser isn't rejected as the past.
    private static readonly TimeSpan PastTolerance = TimeSpan.FromMinutes(1);

    public async Task<PricingOverviewDto> GetCurrentAsync(CancellationToken ct)
    {
        var now = Now();

        // For each spot type: the rule with the latest start time that has already begun.
        var rules = await db.PricingRules.AsNoTracking()
            .Where(r => r.EffectiveFromUtc == db.PricingRules
                .Where(x => x.SpotType == r.SpotType && x.EffectiveFromUtc <= now)
                .Max(x => x.EffectiveFromUtc))
            .OrderBy(r => r.SpotType)
            .Select(r => new PricingRuleDto(r.SpotType, r.HourlyRate, r.DailyCap, r.FreeMinutes, r.EffectiveFromUtc))
            .ToListAsync(ct);

        var fee = await CurrentLostTicketFee(now).Select(f => new LostTicketFeeDto(f.Amount, f.EffectiveFromUtc)).FirstOrDefaultAsync(ct);
        return new PricingOverviewDto(rules, fee);
    }

    public async Task<Result<PricingRuleDto>> SetRuleAsync(SpotType spotType, SavePricingRuleRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(spotType)) return Error.Validation("pricing.invalid_spot_type", "Unknown spot type.");

        var now = Now();
        var effective = ResolveEffective(request.EffectiveFromUtc, now);
        if (effective is null) return Error.Validation("pricing.effective_in_past", "A price change cannot start in the past.");

        if (await db.PricingRules.AnyAsync(r => r.SpotType == spotType && r.EffectiveFromUtc == effective, ct))
            return Error.Conflict("pricing.already_scheduled", "A price for this spot type already starts at that exact time.");

        var current = await db.PricingRules.AsNoTracking()
            .Where(r => r.SpotType == spotType && r.EffectiveFromUtc <= now)
            .OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefaultAsync(ct);

        // History is append-only: existing rows are never edited, so old prices stay reproducible.
        var rule = new PricingRule(spotType, request.HourlyRate, request.DailyCap, request.FreeMinutes, effective.Value);
        db.PricingRules.Add(rule);
        audit.Log("Pricing.RuleChanged", nameof(PricingRule), rule.Id,
            current is null ? null : Snapshot(current), Snapshot(rule));
        await db.SaveChangesAsync(ct);

        return ToDto(rule);
    }

    public async Task<Result<LostTicketFeeDto>> SetLostTicketFeeAsync(SaveLostTicketFeeRequest request, CancellationToken ct)
    {
        var now = Now();
        var effective = ResolveEffective(request.EffectiveFromUtc, now);
        if (effective is null) return Error.Validation("pricing.effective_in_past", "A price change cannot start in the past.");

        if (await db.LostTicketFees.AnyAsync(f => f.EffectiveFromUtc == effective, ct))
            return Error.Conflict("pricing.already_scheduled", "A lost ticket fee already starts at that exact time.");

        var current = await CurrentLostTicketFee(now).FirstOrDefaultAsync(ct);
        var fee = new LostTicketFee(request.Amount, effective.Value);
        db.LostTicketFees.Add(fee);
        audit.Log("Pricing.LostTicketFeeChanged", nameof(LostTicketFee), fee.Id,
            current is null ? null : new { current.Amount, current.EffectiveFromUtc }, new { fee.Amount, fee.EffectiveFromUtc });
        await db.SaveChangesAsync(ct);

        return new LostTicketFeeDto(fee.Amount, fee.EffectiveFromUtc);
    }

    public async Task<Result<PricingTerms>> GetTermsAsync(SpotType spotType, DateTime atUtc, CancellationToken ct)
    {
        var rule = await db.PricingRules.AsNoTracking()
            .Where(r => r.SpotType == spotType && r.EffectiveFromUtc <= atUtc)
            .OrderByDescending(r => r.EffectiveFromUtc).FirstOrDefaultAsync(ct);

        return rule is null
            ? Error.Conflict("pricing.not_configured", $"No price is configured for {spotType} spots.")
            : rule.ToTerms();
    }

    public async Task<Result<decimal>> GetLostTicketFeeAsync(DateTime atUtc, CancellationToken ct)
    {
        var fee = await CurrentLostTicketFee(atUtc).FirstOrDefaultAsync(ct);
        return fee is null ? Error.Conflict("pricing.not_configured", "No lost ticket fee is configured.") : fee.Amount;
    }

    private IQueryable<LostTicketFee> CurrentLostTicketFee(DateTime atUtc)
        => db.LostTicketFees.AsNoTracking().Where(f => f.EffectiveFromUtc <= atUtc).OrderByDescending(f => f.EffectiveFromUtc).Take(1);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static DateTime? ResolveEffective(DateTime? requested, DateTime now)
    {
        if (requested is null) return now;
        var utc = requested.Value.Kind == DateTimeKind.Local
            ? requested.Value.ToUniversalTime()
            : DateTime.SpecifyKind(requested.Value, DateTimeKind.Utc);
        return utc < now - PastTolerance ? null : (utc < now ? now : utc);
    }

    private static object Snapshot(PricingRule r) => new { r.SpotType, r.HourlyRate, r.DailyCap, r.FreeMinutes, r.EffectiveFromUtc };
    private static PricingRuleDto ToDto(PricingRule r) => new(r.SpotType, r.HourlyRate, r.DailyCap, r.FreeMinutes, r.EffectiveFromUtc);
}
