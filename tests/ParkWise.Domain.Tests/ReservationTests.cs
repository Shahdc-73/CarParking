using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Tests;

public class ReservationTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Reservation New(DateTime? start = null, DateTime? end = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start ?? Now.AddHours(1), end ?? Now.AddHours(3), Now);

    [Fact]
    public void End_must_be_after_start()
        => Assert.Throws<DomainException>(() => New(Now.AddHours(2), Now.AddHours(2)));

    [Fact]
    public void Cannot_start_in_the_past()
        => Assert.Throws<DomainException>(() => New(Now.AddMinutes(-1), Now.AddHours(1)));

    [Fact]
    public void Hold_expires_fifteen_minutes_after_start()
        => Assert.Equal(Now.AddHours(1).AddMinutes(15), New().HoldExpiresAtUtc);

    [Fact]
    public void Does_not_expire_at_exactly_start_plus_14_minutes_59()
    {
        var r = New();
        Assert.False(r.TryExpire(r.HoldExpiresAtUtc.AddSeconds(-1)));
        Assert.Equal(ReservationStatus.Pending, r.Status);
    }

    [Fact]
    public void Expires_at_hold_deadline()
    {
        var r = New();
        Assert.True(r.TryExpire(r.HoldExpiresAtUtc));
        Assert.Equal(ReservationStatus.Expired, r.Status);
    }

    [Fact]
    public void Fulfilled_reservation_never_expires()
    {
        var r = New();
        r.Fulfill();
        Assert.False(r.TryExpire(r.HoldExpiresAtUtc.AddDays(1)));
        Assert.Equal(ReservationStatus.Fulfilled, r.Status);
    }

    [Fact]
    public void Cancelled_reservation_cannot_be_fulfilled()
    {
        var r = New();
        r.Cancel();
        Assert.Throws<DomainException>(r.Fulfill);
    }

    [Theory]
    [InlineData(0, 2, 1, 3, true)]   // partial overlap
    [InlineData(0, 4, 1, 2, true)]   // containment
    [InlineData(0, 2, 2, 4, false)]  // back-to-back is allowed
    [InlineData(0, 1, 3, 4, false)]  // disjoint
    public void Overlap_uses_half_open_intervals(int a1, int a2, int b1, int b2, bool expected)
        => Assert.Equal(expected, Reservation.Overlaps(Now.AddHours(a1), Now.AddHours(a2), Now.AddHours(b1), Now.AddHours(b2)));
}

public class ReservationRulesTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Reservation New(TimeSpan startIn, TimeSpan length)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now + startIn, Now + startIn + length, Now);

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(24 * 60, true)]
    [InlineData(24 * 60 + 1, false)]
    public void Duration_must_be_between_30_minutes_and_24_hours(int minutes, bool valid)
    {
        if (valid) New(TimeSpan.FromHours(1), TimeSpan.FromMinutes(minutes));
        else Assert.Throws<DomainException>(() => New(TimeSpan.FromHours(1), TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Cannot_be_booked_more_than_30_days_ahead()
    {
        New(TimeSpan.FromDays(30), TimeSpan.FromHours(1));
        Assert.Throws<DomainException>(() => New(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_reservation_may_start_right_now()
        => Assert.True(New(TimeSpan.Zero, TimeSpan.FromHours(1)).IsPending);

    [Fact]
    public void Arrival_window_opens_15_minutes_before_start_and_closes_when_the_hold_ends()
    {
        var r = New(TimeSpan.FromHours(2), TimeSpan.FromHours(2)); // starts 12:00
        var start = r.StartUtc;

        Assert.False(r.IsInArrivalWindow(start.AddMinutes(-16)));
        Assert.True(r.IsInArrivalWindow(start.AddMinutes(-15)));
        Assert.True(r.IsInArrivalWindow(start));
        Assert.True(r.IsInArrivalWindow(start.AddMinutes(14).AddSeconds(59)));
        Assert.False(r.IsInArrivalWindow(start.AddMinutes(15)));
    }

    [Fact]
    public void The_hold_is_active_only_from_the_start_time_until_it_expires()
    {
        var r = New(TimeSpan.FromHours(2), TimeSpan.FromHours(2));

        Assert.False(r.IsHoldActive(r.StartUtc.AddSeconds(-1)));   // booked but the spot is not blocked yet
        Assert.True(r.IsHoldActive(r.StartUtc));
        Assert.False(r.IsHoldActive(r.HoldExpiresAtUtc));
    }

    [Fact]
    public void Only_pending_reservations_have_windows_or_holds_or_can_move()
    {
        var r = New(TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        r.Cancel();

        Assert.False(r.IsInArrivalWindow(r.StartUtc));
        Assert.False(r.IsHoldActive(r.StartUtc));
        Assert.Throws<DomainException>(() => r.Reassign(Guid.NewGuid()));
    }

    [Fact]
    public void Reassign_moves_a_pending_reservation_to_another_spot()
    {
        var r = New(TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        var other = Guid.NewGuid();
        r.Reassign(other);
        Assert.Equal(other, r.SpotId);
    }

    [Fact]
    public void Holds_of_back_to_back_bookings_on_one_spot_never_overlap()
    {
        var first = New(TimeSpan.FromHours(1), TimeSpan.FromMinutes(30));     // the shortest allowed booking
        var second = new Reservation(Guid.NewGuid(), Guid.NewGuid(), first.SpotId, first.EndUtc, first.EndUtc.AddHours(1), Now);

        Assert.True(first.HoldExpiresAtUtc <= first.EndUtc);
        Assert.True(first.HoldExpiresAtUtc <= second.StartUtc);
    }
}
