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
