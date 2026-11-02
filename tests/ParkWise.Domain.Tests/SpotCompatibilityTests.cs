using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Domain.Tests;

public class SpotCompatibilityTests
{
    private static Vehicle V(VehicleType t, bool permit = false) => new("AB123", t, "Red", hasDisabledPermit: permit);

    [Fact]
    public void Car_only_fits_standard()
        => Assert.Equal([SpotType.Standard], SpotCompatibility.PreferredSpotTypes(V(VehicleType.Car), false));

    [Fact]
    public void Small_car_prefers_compact_then_standard()
        => Assert.Equal([SpotType.Compact, SpotType.Standard], SpotCompatibility.PreferredSpotTypes(V(VehicleType.SmallCar), false));

    [Fact]
    public void Electric_car_prefers_ev_then_standard()
        => Assert.Equal([SpotType.EV, SpotType.Standard], SpotCompatibility.PreferredSpotTypes(V(VehicleType.ElectricCar), false));

    [Fact]
    public void Motorcycle_only_gets_motorcycle_spots_unless_overflow_allowed()
    {
        Assert.Equal([SpotType.Motorcycle], SpotCompatibility.PreferredSpotTypes(V(VehicleType.Motorcycle), false));
        Assert.Equal([SpotType.Motorcycle, SpotType.Compact, SpotType.Standard],
            SpotCompatibility.PreferredSpotTypes(V(VehicleType.Motorcycle), true));
    }

    [Fact]
    public void Disabled_spots_require_a_permit()
    {
        Assert.False(SpotCompatibility.IsCompatible(V(VehicleType.Car), SpotType.Disabled, true));
        Assert.Equal(SpotType.Disabled, SpotCompatibility.PreferredSpotTypes(V(VehicleType.Car, permit: true), false)[0]);
    }

    [Fact]
    public void Car_never_fits_motorcycle_or_ev_spots()
    {
        Assert.False(SpotCompatibility.IsCompatible(V(VehicleType.Car), SpotType.Motorcycle, true));
        Assert.False(SpotCompatibility.IsCompatible(V(VehicleType.Car), SpotType.EV, true));
    }
}
