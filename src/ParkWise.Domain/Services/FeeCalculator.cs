using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Services;

public sealed record PricingTerms(decimal HourlyRate, decimal DailyCap, int FreeMinutes);

/// <summary>How a fee was reached. Stored (as JSON) on the receipt so it never changes when prices change later.</summary>
public sealed record FeeBreakdown(
    decimal Total,
    TimeSpan Duration,
    int FreeMinutes,
    decimal HourlyRate,
    decimal DailyCap,
    int BilledHours,
    int DaysStarted,
    int DaysCapped,
    bool IsLostTicket);

/// <summary>
/// Pure fee rules: no clock, no database, no framework. Same input, same output, so it is fully unit-testable.
///
/// 1. The first <c>FreeMinutes</c> of the stay are free (they are deducted, not a grace period: a 16 minute stay
///    bills 1 minute, which rounds up to 1 hour).
/// 2. What remains is billed per STARTED hour (rounded up).
/// 3. The stay is split into consecutive 24-hour blocks counted from check-in. Each block costs at most
///    <c>DailyCap</c>. Calendar midnight has no effect: crossing it never costs extra.
/// 4. Lost ticket: a flat fee that replaces the normal fee.
/// </summary>
public static class FeeCalculator
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly long HourTicks = TimeSpan.FromHours(1).Ticks;

    public static FeeBreakdown Calculate(DateTime checkInUtc, DateTime checkOutUtc, PricingTerms terms)
    {
        if (checkOutUtc < checkInUtc) throw new DomainException("Check-out cannot be before check-in.");
        if (terms.HourlyRate < 0 || terms.DailyCap < 0 || terms.FreeMinutes < 0)
            throw new DomainException("Pricing terms cannot be negative.");

        var duration = checkOutUtc - checkInUtc;
        var free = TimeSpan.FromMinutes(terms.FreeMinutes);

        // Number of 24h blocks started (0 for an instant stay): ceil(duration / 24h) using integer ticks.
        var blocks = duration <= TimeSpan.Zero ? 0L : (duration.Ticks + Day.Ticks - 1) / Day.Ticks;

        decimal total = 0;
        var hours = 0;
        var capped = 0;

        if (blocks >= 1)
        {
            // The free time is used up in the first block only.
            var first = Block(Min(duration, Day) - free, terms);
            total += first.Fee; hours += first.Hours; if (first.Capped) capped++;

            if (blocks >= 2)
            {
                var full = Block(Day, terms);
                var middle = (int)(blocks - 2);
                total += checked(middle * full.Fee); hours += checked(middle * full.Hours); if (full.Capped) capped += middle;

                var last = Block(duration - Day * (blocks - 1), terms);
                total += last.Fee; hours += last.Hours; if (last.Capped) capped++;
            }
        }

        return new FeeBreakdown(Money(total), duration, terms.FreeMinutes, terms.HourlyRate, terms.DailyCap,
            hours, (int)blocks, capped, IsLostTicket: false);
    }

    public static FeeBreakdown LostTicket(DateTime checkInUtc, DateTime checkOutUtc, decimal flatFee)
    {
        if (checkOutUtc < checkInUtc) throw new DomainException("Check-out cannot be before check-in.");
        if (flatFee < 0) throw new DomainException("Lost ticket fee cannot be negative.");

        return new FeeBreakdown(Money(flatFee), checkOutUtc - checkInUtc, 0, 0, 0, 0, 0, 0, IsLostTicket: true);
    }

    private static (decimal Fee, int Hours, bool Capped) Block(TimeSpan billable, PricingTerms terms)
    {
        if (billable <= TimeSpan.Zero) return (0, 0, false);

        var hours = (int)((billable.Ticks + HourTicks - 1) / HourTicks); // started hours
        var raw = hours * terms.HourlyRate;
        return raw > terms.DailyCap ? (terms.DailyCap, hours, true) : (raw, hours, false);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;

    /// <summary>Money is rounded half away from zero to 2 decimals (the way a cashier rounds), never banker's rounding.</summary>
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
