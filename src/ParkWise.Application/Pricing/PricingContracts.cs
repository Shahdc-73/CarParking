using FluentValidation;
using ParkWise.Application.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Pricing;

public sealed record PricingRuleDto(SpotType SpotType, decimal HourlyRate, decimal DailyCap, int FreeMinutes, DateTime EffectiveFromUtc);
public sealed record LostTicketFeeDto(decimal Amount, DateTime EffectiveFromUtc);
public sealed record PricingOverviewDto(IReadOnlyList<PricingRuleDto> Rules, LostTicketFeeDto? LostTicketFee);

/// <param name="EffectiveFromUtc">When the new price starts. Null means "now". Must not be in the past.</param>
public sealed record SavePricingRuleRequest(decimal HourlyRate, decimal DailyCap, int FreeMinutes, DateTime? EffectiveFromUtc);
public sealed record SaveLostTicketFeeRequest(decimal Amount, DateTime? EffectiveFromUtc);

public interface IPricingService
{
    Task<PricingOverviewDto> GetCurrentAsync(CancellationToken ct);
    Task<Result<PricingRuleDto>> SetRuleAsync(SpotType spotType, SavePricingRuleRequest request, CancellationToken ct);
    Task<Result<LostTicketFeeDto>> SetLostTicketFeeAsync(SaveLostTicketFeeRequest request, CancellationToken ct);

    /// <summary>The rule in force at a given moment (newest rule whose start time has passed).</summary>
    Task<Result<PricingTerms>> GetTermsAsync(SpotType spotType, DateTime atUtc, CancellationToken ct);
    Task<Result<decimal>> GetLostTicketFeeAsync(DateTime atUtc, CancellationToken ct);
}

public sealed class SavePricingRuleRequestValidator : AbstractValidator<SavePricingRuleRequest>
{
    public SavePricingRuleRequestValidator()
    {
        RuleFor(x => x.HourlyRate).InclusiveBetween(0m, 1000m).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.DailyCap).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .GreaterThanOrEqualTo(x => x.HourlyRate).WithMessage("Daily cap cannot be lower than the hourly rate.")
            .LessThanOrEqualTo(100_000m);
        RuleFor(x => x.FreeMinutes).InclusiveBetween(0, 240);
    }
}

public sealed class SaveLostTicketFeeRequestValidator : AbstractValidator<SaveLostTicketFeeRequest>
{
    public SaveLostTicketFeeRequestValidator()
        => RuleFor(x => x.Amount).InclusiveBetween(0m, 100_000m).PrecisionScale(18, 2, ignoreTrailingZeros: true);
}
