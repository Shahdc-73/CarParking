namespace ParkWise.Application.Reports;

/// <summary>
/// The calendar maths of reports, as pure functions so the tricky parts (which local day does 23:30 UTC belong to?) are unit-tested.
/// "Local" always means the viewer's fixed UTC offset; daylight-saving changes inside a range are deliberately not modelled.
/// </summary>
public static class ReportMath
{
    public static DateOnly LocalDay(DateTime utc, int utcOffsetMinutes)
        => DateOnly.FromDateTime(utc.AddMinutes(utcOffsetMinutes));

    public static int LocalHour(DateTime utc, int utcOffsetMinutes)
        => utc.AddMinutes(utcOffsetMinutes).Hour;

    /// <summary>The UTC instant at which a LOCAL day begins.</summary>
    public static DateTime DayStartUtc(DateOnly day, int utcOffsetMinutes)
        => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue).AddMinutes(-utcOffsetMinutes), DateTimeKind.Utc);

    /// <summary>[start, end) in UTC covering every local day from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) RangeUtc(DateOnly from, DateOnly to, int utcOffsetMinutes)
        => (DayStartUtc(from, utcOffsetMinutes), DayStartUtc(to.AddDays(1), utcOffsetMinutes));

    /// <summary>One entry per day in the range, including days with no revenue, so a chart has no holes.</summary>
    public static IReadOnlyList<RevenueDayDto> RevenueByDay(DateOnly from, DateOnly to, IEnumerable<(DateTime IssuedAtUtc, decimal Amount)> receipts, int utcOffsetMinutes)
    {
        var perDay = receipts
            .GroupBy(r => LocalDay(r.IssuedAtUtc, utcOffsetMinutes))
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(r => r.Amount), Count: g.Count()));

        var days = new List<RevenueDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (amount, count) = perDay.GetValueOrDefault(day);
            days.Add(new RevenueDayDto(day, amount, count));
        }
        return days;
    }

    /// <summary>Check-ins per local hour of day: always 24 entries (0-23).</summary>
    public static IReadOnlyList<PeakHourDto> PeakHours(IEnumerable<DateTime> checkInsUtc, int utcOffsetMinutes)
    {
        var counts = new int[24];
        foreach (var at in checkInsUtc) counts[LocalHour(at, utcOffsetMinutes)]++;
        return counts.Select((count, hour) => new PeakHourDto(hour, count)).ToList();
    }

    public static double OccupancyRate(int occupied, int total, int outOfService)
    {
        var usable = total - outOfService;
        return usable <= 0 ? 0 : Math.Round((double)occupied / usable, 4);
    }
}
