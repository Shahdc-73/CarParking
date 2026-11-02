using FluentValidation.TestHelper;
using ParkWise.Application.Common;
using ParkWise.Application.Pricing;
using ParkWise.Application.Sessions;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Tests;

public class CodeGeneratorTests
{
    [Fact]
    public void Tickets_have_a_fixed_readable_format()
        => Assert.All(Enumerable.Range(0, 500).Select(_ => CodeGenerator.Ticket()),
            code => Assert.Matches("^PW-[ABCDEFGHJKMNPQRSTVWXYZ23456789]{6}$", code));

    [Fact]
    public void Tickets_never_use_easily_confused_characters()
        => Assert.All(Enumerable.Range(0, 500).Select(_ => CodeGenerator.Ticket()[3..]),
            code => Assert.DoesNotContain(code, c => "ILOU01".Contains(c)));

    [Fact]
    public void Tickets_do_not_repeat_in_practice()
        => Assert.Equal(5000, Enumerable.Range(0, 5000).Select(_ => CodeGenerator.Ticket()).Distinct().Count());

    [Fact]
    public void Receipt_numbers_carry_the_issue_date()
        => Assert.Matches("^RC-20260317-[A-Z2-9]{6}$", CodeGenerator.ReceiptNumber(new DateTime(2026, 3, 17, 23, 59, 0, DateTimeKind.Utc)));
}

public class SessionAndPricingValidatorTests
{
    [Theory]
    [InlineData("AB-123", true)]
    [InlineData("!", false)]
    [InlineData("", false)]
    public void Check_in_requires_a_usable_plate_and_lot(string plate, bool valid)
    {
        var result = new CheckInRequestValidator().TestValidate(new CheckInRequest(Guid.NewGuid(), plate, VehicleType.Car));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Check_in_requires_a_lot()
        => new CheckInRequestValidator().TestValidate(new CheckInRequest(Guid.Empty, "AB123", null)).ShouldHaveValidationErrorFor(x => x.LotId);

    [Fact]
    public void Vehicle_type_is_optional_for_check_in_but_must_be_valid_when_given()
    {
        var v = new CheckInRequestValidator();
        Assert.True(v.TestValidate(new CheckInRequest(Guid.NewGuid(), "AB123", null)).IsValid);
        Assert.False(v.TestValidate(new CheckInRequest(Guid.NewGuid(), "AB123", (VehicleType)42)).IsValid);
    }

    [Theory]
    [InlineData(3.00, 20.00, 15, true)]
    [InlineData(0, 0, 0, true)]               // a free lot
    [InlineData(3.00, 3.00, 15, true)]        // cap equal to rate: flat daily price
    [InlineData(3.00, 2.99, 15, false)]       // cap below rate
    [InlineData(-0.01, 5, 15, false)]
    [InlineData(3.005, 20, 15, false)]        // sub-cent precision
    [InlineData(3.00, 20.00, 241, false)]
    [InlineData(3.00, 20.00, -1, false)]
    [InlineData(1000.01, 2000, 15, false)]
    public void Pricing_rules(double rate, double cap, int free, bool valid)
    {
        var result = new SavePricingRuleRequestValidator().TestValidate(new SavePricingRuleRequest((decimal)rate, (decimal)cap, free, null));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    [InlineData(10.555, false)]
    public void Lost_ticket_fee(double amount, bool valid)
        => Assert.Equal(valid, new SaveLostTicketFeeRequestValidator().TestValidate(new SaveLostTicketFeeRequest((decimal)amount, null)).IsValid);
}
