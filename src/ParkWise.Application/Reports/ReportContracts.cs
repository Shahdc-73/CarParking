using FluentValidation;
using ParkWise.Application.Common;

namespace ParkWise.Application.Reports;

/// <param name="OccupancyRate">Occupied spots / usable spots (spots that are out of service cannot be used, so they are not counted). 0 to 1.</param>
public sealed record LotOccupancyDto(
    Guid LotId, string LotName, bool IsActive, int Total, int Available, int Occupied, int Reserved, int OutOfService, double OccupancyRate);

public sealed record OccupancyReportDto(DateTime GeneratedAtUtc, LotOccupancyDto Overall, IReadOnlyList<LotOccupancyDto> Lots);

/// <param name="From">First LOCAL day (inclusive).</param>
/// <param name="To">Last LOCAL day (inclusive).</param>
/// <param name="UtcOffsetMinutes">The viewer's offset from UTC (e.g. 120 for UTC+2). Decides which local day or hour a moment belongs to.</param>
public sealed record PerformanceQuery(DateOnly From, DateOnly To, Guid? LotId, int UtcOffsetMinutes = 0)
{
    public const int MaxDays = 92;

    public int Days => To.DayNumber - From.DayNumber + 1;
}

public sealed record RevenueDayDto(DateOnly Date, decimal Amount, int Sessions);

public sealed record PeakHourDto(int Hour, int CheckIns);

public sealed record PerformanceReportDto(
    DateOnly From, DateOnly To, Guid? LotId, int UtcOffsetMinutes,
    decimal TotalRevenue, int PaidSessions, int LostTickets, int CheckIns,
    double? AverageStayMinutes, double? LongestStayMinutes,
    IReadOnlyList<RevenueDayDto> RevenueByDay, IReadOnlyList<PeakHourDto> PeakHours);

public interface IReportService
{
    Task<OccupancyReportDto> GetOccupancyAsync(CancellationToken ct);
    Task<Result<PerformanceReportDto>> GetPerformanceAsync(PerformanceQuery query, CancellationToken ct);
}

public sealed class PerformanceQueryValidator : AbstractValidator<PerformanceQuery>
{
    public PerformanceQueryValidator()
    {
        // A missing query parameter binds to the default value (0001-01-01), not to an error: treat that as "not provided".
        RuleFor(x => x.From).NotEqual(default(DateOnly)).WithMessage("The start date is required.");
        RuleFor(x => x.To).NotEqual(default(DateOnly)).WithMessage("The end date is required.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date cannot be before the start date.");
        RuleFor(x => x).Must(q => q.Days <= PerformanceQuery.MaxDays)
            .When(q => q.To >= q.From).WithName("to")
            .WithMessage($"Choose a range of at most {PerformanceQuery.MaxDays} days.");
        RuleFor(x => x.UtcOffsetMinutes).InclusiveBetween(-14 * 60, 14 * 60).WithMessage("The UTC offset must be between -14:00 and +14:00.");
    }
}
