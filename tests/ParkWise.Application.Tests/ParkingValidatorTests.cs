using FluentValidation.TestHelper;
using ParkWise.Application.Parking;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Tests;

public class ParkingValidatorTests
{
    [Theory]
    [InlineData("A-01", true)]
    [InlineData("a1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("bad_underscore", false)]
    [InlineData("123456789012345678901", false)] // 21 chars
    public void Spot_codes(string code, bool valid)
    {
        var result = new CreateSpotRequestValidator().TestValidate(new CreateSpotRequest(code, SpotType.Standard));
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.Code);
        else result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Undefined_spot_type_is_rejected()
        => new CreateSpotRequestValidator().TestValidate(new CreateSpotRequest("A-01", (SpotType)99))
            .ShouldHaveValidationErrorFor(x => x.Type);

    [Theory]
    [InlineData("", "Addr", false)]
    [InlineData("Lot", "", false)]
    [InlineData("Lot", "Addr", true)]
    public void Lots_need_a_name_and_address(string name, string address, bool valid)
    {
        var result = new SaveLotRequestValidator().TestValidate(new SaveLotRequest(name, address, false));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(200, true)]
    [InlineData(-11, false)]
    [InlineData(201, false)]
    public void Level_numbers_are_bounded(int number, bool valid)
    {
        var result = new CreateLevelRequestValidator().TestValidate(new CreateLevelRequest(number, "L"));
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.Number);
        else result.ShouldHaveValidationErrorFor(x => x.Number);
    }

    [Theory]
    [InlineData("AB-123 C", true)]
    [InlineData("12", true)]
    [InlineData("A", false)]
    [InlineData("!!", false)]
    [InlineData("TOOLONGPLATE", false)]
    public void Plates(string plate, bool valid)
    {
        var result = new CreateVehicleRequestValidator().TestValidate(new CreateVehicleRequest(plate, VehicleType.Car, "Red"));
        if (valid) result.ShouldNotHaveValidationErrorFor(x => x.PlateNumber);
        else result.ShouldHaveValidationErrorFor(x => x.PlateNumber);
    }

    [Fact]
    public void Vehicle_color_is_required_and_bounded()
    {
        var v = new CreateVehicleRequestValidator();
        v.TestValidate(new CreateVehicleRequest("AB123", VehicleType.Car, "")).ShouldHaveValidationErrorFor(x => x.Color);
        v.TestValidate(new CreateVehicleRequest("AB123", VehicleType.Car, new string('x', 31))).ShouldHaveValidationErrorFor(x => x.Color);
    }
}
