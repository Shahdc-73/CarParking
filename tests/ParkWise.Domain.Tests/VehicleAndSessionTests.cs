using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Tests;

public class VehicleAndSessionTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ab-123 c", "AB123C")]
    [InlineData(" xy 99 ", "XY99")]
    public void Plate_is_normalized(string input, string expected)
        => Assert.Equal(expected, Vehicle.NormalizePlate(input));

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("TOOLONGPLATE1")]
    public void Invalid_plates_are_rejected(string input)
        => Assert.ThrowsAny<Exception>(() => Vehicle.NormalizePlate(input));

    private static ParkingSession NewSession() => new(Guid.NewGuid(), Guid.NewGuid(), "T-1", T0, Guid.NewGuid());

    [Fact]
    public void Checkout_closes_the_session()
    {
        var s = NewSession();
        s.CheckOut(T0.AddHours(2));
        Assert.Equal(SessionStatus.Completed, s.Status);
        Assert.Equal(T0.AddHours(2), s.CheckOutAtUtc);
    }

    [Fact]
    public void Checkout_twice_is_rejected_at_domain_level()
    {
        var s = NewSession();
        s.CheckOut(T0.AddHours(1));
        Assert.Throws<DomainException>(() => s.CheckOut(T0.AddHours(2)));
    }

    [Fact]
    public void Checkout_before_checkin_is_rejected()
        => Assert.Throws<DomainException>(() => NewSession().CheckOut(T0.AddMinutes(-1)));

    [Fact]
    public void Lost_ticket_checkout_sets_lost_ticket_status()
    {
        var s = NewSession();
        s.CheckOut(T0.AddHours(1), lostTicket: true);
        Assert.Equal(SessionStatus.LostTicket, s.Status);
    }

    [Fact]
    public void Pricing_rule_validates_inputs()
    {
        Assert.Throws<DomainException>(() => new PricingRule(SpotType.Standard, -1, 10, 15, T0));
        Assert.Throws<DomainException>(() => new PricingRule(SpotType.Standard, 5, 4, 15, T0));
        Assert.Throws<DomainException>(() => new PricingRule(SpotType.Standard, 5, 20, -1, T0));
    }
}
