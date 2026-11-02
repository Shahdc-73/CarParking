using ParkWise.Domain.Exceptions;
using ParkWise.Domain.Services;

namespace ParkWise.Domain.Tests;

public class FeeCalculatorTests
{
    // Seed pricing for Standard spots: 3.00/hour, 20.00 daily cap, first 15 minutes free.
    private static readonly PricingTerms Standard = new(3.00m, 20.00m, 15);
    private static readonly DateTime In = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static FeeBreakdown Fee(TimeSpan stay, PricingTerms? terms = null, DateTime? checkIn = null)
    {
        var start = checkIn ?? In;
        return FeeCalculator.Calculate(start, start + stay, terms ?? Standard);
    }

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);
    private static TimeSpan Hrs(double h) => TimeSpan.FromHours(h);

    [Theory]
    [InlineData(0, 0.00)]        // instant
    [InlineData(1, 0.00)]
    [InlineData(14, 0.00)]
    [InlineData(15, 0.00)]       // exactly the free period
    public void Stays_within_the_free_period_cost_nothing(double minutes, double expected)
        => Assert.Equal((decimal)expected, Fee(Min(minutes)).Total);

    [Fact]
    public void One_second_past_the_free_period_starts_the_first_paid_hour()
        => Assert.Equal(3.00m, Fee(Min(15) + TimeSpan.FromSeconds(1)).Total);

    [Theory]
    [InlineData(16, 3.00)]       // 1 billable minute -> 1 started hour
    [InlineData(60, 3.00)]       // 45 billable minutes
    [InlineData(75, 3.00)]       // exactly 1 billable hour
    [InlineData(76, 6.00)]       // 1h01m billable -> 2 started hours
    [InlineData(120, 6.00)]
    [InlineData(135, 6.00)]
    [InlineData(136, 9.00)]
    [InlineData(375, 18.00)]     // 6h billable exactly
    public void Hours_are_billed_per_started_hour_after_the_free_period(double minutes, double expected)
        => Assert.Equal((decimal)expected, Fee(Min(minutes)).Total);

    [Fact]
    public void Seventy_five_minutes_and_one_second_rounds_up_to_two_hours()
        => Assert.Equal(6.00m, Fee(Min(75) + TimeSpan.FromSeconds(1)).Total);

    [Fact]
    public void Daily_cap_limits_a_long_day()
    {
        var fee = Fee(Hrs(10)); // 9h45 billable = 10h * 3 = 30 -> capped at 20
        Assert.Equal(20.00m, fee.Total);
        Assert.Equal(1, fee.DaysCapped);
        Assert.Equal(10, fee.BilledHours);
    }

    [Fact]
    public void A_stay_just_under_the_cap_is_not_capped()
    {
        var fee = Fee(Hrs(6) + Min(15)); // 6h billable = 18.00 < 20
        Assert.Equal(18.00m, fee.Total);
        Assert.Equal(0, fee.DaysCapped);
    }

    [Fact]
    public void The_cap_applies_exactly_when_the_hours_cost_more_than_it()
    {
        Assert.Equal(18.00m, Fee(Hrs(6) + Min(15)).Total);  // 6h = 18.00
        Assert.Equal(20.00m, Fee(Hrs(7) + Min(15)).Total);  // 7h = 21.00 -> 20.00
    }

    [Theory]
    [InlineData(24, 20.00, 1, 1)]        // a full day: capped
    [InlineData(25, 23.00, 2, 1)]        // 20 + 1 started hour (3.00) in day 2
    [InlineData(48, 40.00, 2, 2)]        // two capped days
    [InlineData(50, 46.00, 3, 2)]        // 20 + 20 + 2h (6.00)
    [InlineData(168, 140.00, 7, 7)]      // a full week
    public void Multi_day_stays_cap_each_24_hour_block_separately(double hours, double expected, int daysStarted, int daysCapped)
    {
        var fee = Fee(Hrs(hours));
        Assert.Equal((decimal)expected, fee.Total);
        Assert.Equal(daysStarted, fee.DaysStarted);
        Assert.Equal(daysCapped, fee.DaysCapped);
    }

    [Fact]
    public void One_second_into_the_second_day_starts_a_new_block()
    {
        var fee = Fee(Hrs(24) + TimeSpan.FromSeconds(1));
        Assert.Equal(23.00m, fee.Total); // 20.00 + 3.00
        Assert.Equal(2, fee.DaysStarted);
    }

    [Fact]
    public void Free_minutes_are_used_only_in_the_first_block()
    {
        // Day 2 has no free time: 1 hour exactly = 3.00, whereas the free 15 minutes only help in day 1.
        Assert.Equal(23.00m, Fee(Hrs(25)).Total);
    }

    [Fact]
    public void Crossing_midnight_costs_exactly_the_same_as_not_crossing_it()
    {
        var beforeMidnight = new DateTime(2026, 3, 10, 23, 30, 0, DateTimeKind.Utc);
        var daytime = new DateTime(2026, 3, 10, 10, 30, 0, DateTimeKind.Utc);

        var crossing = Fee(Hrs(1), checkIn: beforeMidnight);   // 23:30 -> 00:30 next day
        var sameDay = Fee(Hrs(1), checkIn: daytime);

        Assert.Equal(sameDay.Total, crossing.Total);
        Assert.Equal(3.00m, crossing.Total);
        Assert.Equal(1, crossing.DaysStarted); // the calendar date changed, but that is not a new billing day
    }

    [Fact]
    public void A_long_overnight_stay_is_charged_per_24_hours_not_per_calendar_date()
    {
        // 22:00 -> 08:00 two days later = 34 hours: 2 billing days, though it touches 3 calendar dates.
        var checkIn = new DateTime(2026, 3, 10, 22, 0, 0, DateTimeKind.Utc);
        var fee = Fee(Hrs(34), checkIn: checkIn);
        Assert.Equal(2, fee.DaysStarted);
        Assert.Equal(40.00m, fee.Total); // 20.00 + (10h = 30 -> capped 20.00)
    }

    [Fact]
    public void Free_minutes_can_be_configured_including_zero()
    {
        var none = new PricingTerms(3.00m, 20.00m, 0);
        Assert.Equal(3.00m, Fee(TimeSpan.FromSeconds(1), none).Total);

        var hour = new PricingTerms(3.00m, 20.00m, 60);
        Assert.Equal(0.00m, Fee(Hrs(1), hour).Total);
        Assert.Equal(3.00m, Fee(Hrs(1) + TimeSpan.FromSeconds(1), hour).Total);
    }

    [Fact]
    public void Cap_equal_to_the_hourly_rate_means_a_flat_daily_price()
    {
        var flat = new PricingTerms(3.00m, 3.00m, 15);
        Assert.Equal(3.00m, Fee(Hrs(5), flat).Total);
        Assert.Equal(6.00m, Fee(Hrs(30), flat).Total);
    }

    [Fact]
    public void A_free_lot_is_always_free()
        => Assert.Equal(0.00m, Fee(Hrs(100), new PricingTerms(0m, 0m, 15)).Total);

    [Fact]
    public void Money_rounds_half_away_from_zero_not_to_even()
    {
        var odd = new PricingTerms(1.005m, 100m, 0);   // 1.005 -> 1.01 (banker's rounding would give 1.00)
        Assert.Equal(1.01m, Fee(Min(30), odd).Total);
    }

    [Fact]
    public void Breakdown_records_how_the_fee_was_reached()
    {
        var fee = Fee(Hrs(2));
        Assert.Equal(Hrs(2), fee.Duration);
        Assert.Equal((15, 3.00m, 20.00m), (fee.FreeMinutes, fee.HourlyRate, fee.DailyCap));
        Assert.Equal(2, fee.BilledHours);   // 1h45 billable -> 2 started hours
        Assert.False(fee.IsLostTicket);
    }

    [Fact]
    public void Checking_out_before_checking_in_is_rejected()
        => Assert.Throws<DomainException>(() => FeeCalculator.Calculate(In, In.AddSeconds(-1), Standard));

    [Theory]
    [InlineData(-1, 10, 15)]
    [InlineData(1, -10, 15)]
    [InlineData(1, 10, -1)]
    public void Negative_terms_are_rejected(double rate, double cap, int free)
        => Assert.Throws<DomainException>(() => FeeCalculator.Calculate(In, In.AddHours(1), new PricingTerms((decimal)rate, (decimal)cap, free)));

    [Fact]
    public void Lost_ticket_charges_the_flat_fee_regardless_of_stay_length()
    {
        var short1 = FeeCalculator.LostTicket(In, In.AddMinutes(5), 50m);
        var long1 = FeeCalculator.LostTicket(In, In.AddDays(9), 50m);

        Assert.Equal(50.00m, short1.Total);
        Assert.Equal(50.00m, long1.Total);   // it REPLACES the normal fee, even on a free-length stay
        Assert.True(long1.IsLostTicket);
        Assert.Equal(TimeSpan.FromDays(9), long1.Duration);
    }

    [Fact]
    public void Lost_ticket_fee_cannot_be_negative()
        => Assert.Throws<DomainException>(() => FeeCalculator.LostTicket(In, In.AddHours(1), -1m));

    [Fact]
    public void Fee_never_decreases_as_the_stay_gets_longer_and_never_exceeds_cap_per_day()
    {
        var previous = 0m;
        for (var minutes = 0; minutes <= 8 * 24 * 60; minutes += 7)
        {
            var fee = Fee(Min(minutes));
            Assert.True(fee.Total >= previous, $"fee dropped at {minutes} minutes: {previous} -> {fee.Total}");
            Assert.True(fee.Total <= Standard.DailyCap * fee.DaysStarted, $"over cap at {minutes} minutes");
            previous = fee.Total;
        }
    }

    [Fact]
    public void Same_input_always_gives_the_same_result()
        => Assert.Equal(Fee(Hrs(37) + Min(11)), Fee(Hrs(37) + Min(11)));
}
