using FluentValidation.TestHelper;
using ParkWise.Application.Reports;

namespace ParkWise.Application.Tests;

public class ReportMathTests
{
    private static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0) => new(2026, month, day, hour, minute, second, DateTimeKind.Utc);

    [Theory]
    [InlineData(23, 30, 120, 2)]    // 23:30 UTC is already the next day at UTC+2
    [InlineData(23, 30, 0, 1)]
    [InlineData(2, 0, -300, 0)]     // 02:00 UTC is still the previous evening at UTC-5
    [InlineData(21, 59, 120, 1)]    // 23:59 local: still today
    [InlineData(22, 0, 120, 2)]     // exactly local midnight: tomorrow
    public void A_moment_belongs_to_the_local_day_of_the_viewer(int hour, int minute, int offset, int expectedDay)
    {
        var day = ReportMath.LocalDay(Utc(6, 1, hour, minute), offset);
        Assert.Equal(new DateOnly(2026, 6, expectedDay == 0 ? 1 : expectedDay).AddDays(expectedDay == 0 ? -1 : 0), day);
    }

    [Theory]
    [InlineData(10, 0, 0, 10)]
    [InlineData(22, 30, 120, 0)]    // wraps past midnight
    [InlineData(1, 0, -300, 20)]    // wraps backwards
    [InlineData(23, 59, 60, 0)]
    public void The_local_hour_follows_the_offset_and_wraps_around_the_clock(int hour, int minute, int offset, int expected)
        => Assert.Equal(expected, ReportMath.LocalHour(Utc(6, 1, hour, minute), offset));

    [Fact]
    public void A_local_day_starts_at_the_matching_utc_instant()
    {
        Assert.Equal(Utc(6, 1, 22), ReportMath.DayStartUtc(new DateOnly(2026, 6, 2), 120));   // local midnight at UTC+2 is 22:00 UTC the day before
        Assert.Equal(Utc(6, 2, 5), ReportMath.DayStartUtc(new DateOnly(2026, 6, 2), -300));
        Assert.Equal(Utc(6, 2, 0), ReportMath.DayStartUtc(new DateOnly(2026, 6, 2), 0));
        Assert.Equal(DateTimeKind.Utc, ReportMath.DayStartUtc(new DateOnly(2026, 6, 2), 0).Kind);
    }

    [Fact]
    public void A_range_covers_whole_local_days_with_an_exclusive_end()
    {
        var (start, end) = ReportMath.RangeUtc(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 3), 120);
        Assert.Equal(Utc(5, 31, 22), start);
        Assert.Equal(Utc(6, 3, 22), end);
        Assert.Equal(TimeSpan.FromDays(3), end - start);
    }

    [Fact]
    public void Revenue_is_summed_per_local_day_and_days_without_revenue_are_still_listed()
    {
        var receipts = new[]
        {
            (Utc(6, 1, 10), 5.00m), (Utc(6, 1, 11), 7.50m),
            (Utc(6, 3, 9), 20.00m),
        };

        var days = ReportMath.RevenueByDay(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 4), receipts, 0);

        Assert.Equal(4, days.Count);
        Assert.Equal(new[] { 12.50m, 0m, 20.00m, 0m }, days.Select(d => d.Amount));
        Assert.Equal(new[] { 2, 0, 1, 0 }, days.Select(d => d.Sessions));
        Assert.Equal(new DateOnly(2026, 6, 1), days[0].Date);
    }

    [Fact]
    public void Revenue_at_the_midnight_boundary_lands_on_the_correct_local_day()
    {
        var receipts = new[] { (Utc(6, 1, 21, 59, 59), 1m), (Utc(6, 1, 22, 0, 0), 2m) };

        var days = ReportMath.RevenueByDay(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), receipts, 120); // UTC+2

        Assert.Equal(new[] { 1m, 2m }, days.Select(d => d.Amount)); // 23:59:59 local is June 1, 00:00:00 local is June 2
    }

    [Fact]
    public void An_empty_range_still_has_one_zero_row_per_day()
    {
        var days = ReportMath.RevenueByDay(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), [], 0);
        Assert.Equal(7, days.Count);
        Assert.All(days, d => Assert.Equal((0m, 0), (d.Amount, d.Sessions)));
    }

    [Fact]
    public void Peak_hours_always_has_24_entries_and_counts_arrivals_per_local_hour()
    {
        var hours = ReportMath.PeakHours([Utc(6, 1, 8, 5), Utc(6, 1, 8, 59), Utc(6, 2, 8, 0), Utc(6, 1, 17, 30)], 0);

        Assert.Equal(24, hours.Count);
        Assert.Equal(Enumerable.Range(0, 24), hours.Select(h => h.Hour));
        Assert.Equal(3, hours[8].CheckIns);
        Assert.Equal(1, hours[17].CheckIns);
        Assert.Equal(4, hours.Sum(h => h.CheckIns));
    }

    [Fact]
    public void Peak_hours_shift_with_the_viewers_offset()
    {
        var hours = ReportMath.PeakHours([Utc(6, 1, 22, 30)], 120); // 00:30 the next day for a UTC+2 viewer
        Assert.Equal(1, hours[0].CheckIns);
        Assert.Equal(0, hours[22].CheckIns);
    }

    [Theory]
    [InlineData(10, 40, 0, 0.25)]
    [InlineData(10, 40, 10, 0.3333)]   // out-of-service spots cannot be used, so they leave the denominator
    [InlineData(40, 40, 0, 1.0)]
    [InlineData(0, 40, 0, 0.0)]
    [InlineData(0, 0, 0, 0.0)]         // an empty lot is not "100% full" or an error
    [InlineData(0, 10, 10, 0.0)]       // every spot out of service
    public void Occupancy_rate(int occupied, int total, int outOfService, double expected)
        => Assert.Equal(expected, ReportMath.OccupancyRate(occupied, total, outOfService));
}

public class PerformanceQueryValidatorTests
{
    private readonly PerformanceQueryValidator _validator = new();
    private static PerformanceQuery Q(string from, string to, int offset = 0) => new(DateOnly.Parse(from), DateOnly.Parse(to), null, offset);

    [Fact]
    public void A_normal_range_is_valid()
        => Assert.True(_validator.TestValidate(Q("2026-06-01", "2026-06-30")).IsValid);

    [Fact]
    public void A_single_day_is_valid()
        => Assert.True(_validator.TestValidate(Q("2026-06-01", "2026-06-01")).IsValid);

    [Fact]
    public void Missing_dates_are_reported_instead_of_silently_meaning_the_year_one()
    {
        var result = _validator.TestValidate(new PerformanceQuery(default, default, null));
        result.ShouldHaveValidationErrorFor(x => x.From);
        result.ShouldHaveValidationErrorFor(x => x.To);
    }

    [Fact]
    public void The_end_cannot_be_before_the_start()
        => _validator.TestValidate(Q("2026-06-05", "2026-06-01")).ShouldHaveValidationErrorFor(x => x.To);

    [Fact]
    public void The_range_is_limited_to_92_days_inclusive()
    {
        Assert.True(_validator.TestValidate(Q("2026-01-01", "2026-04-02")).IsValid);    // exactly 92 days
        Assert.False(_validator.TestValidate(Q("2026-01-01", "2026-04-03")).IsValid);   // 93 days
    }

    [Theory]
    [InlineData(840, true)]
    [InlineData(-840, true)]
    [InlineData(841, false)]
    [InlineData(-841, false)]
    public void The_utc_offset_must_be_a_real_time_zone_offset(int offset, bool valid)
        => Assert.Equal(valid, _validator.TestValidate(Q("2026-06-01", "2026-06-02", offset)).IsValid);
}
