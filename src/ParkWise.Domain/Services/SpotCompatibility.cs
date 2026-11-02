using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Domain.Services;

/// <summary>Pure rules for which spot types a vehicle may use, best fit first.</summary>
public static class SpotCompatibility
{
    public static IReadOnlyList<SpotType> PreferredSpotTypes(Vehicle vehicle, bool allowMotorcycleOverflow)
    {
        var types = new List<SpotType>();

        // Permit holders get Disabled spots first; nobody else may ever use them.
        if (vehicle.HasDisabledPermit) types.Add(SpotType.Disabled);

        switch (vehicle.Type)
        {
            case VehicleType.Car:
                types.Add(SpotType.Standard);
                break;
            case VehicleType.SmallCar:
                types.AddRange([SpotType.Compact, SpotType.Standard]);
                break;
            case VehicleType.ElectricCar:
                types.AddRange([SpotType.EV, SpotType.Standard]);
                break;
            case VehicleType.Motorcycle:
                types.Add(SpotType.Motorcycle);
                if (allowMotorcycleOverflow) types.AddRange([SpotType.Compact, SpotType.Standard]);
                break;
        }

        return types;
    }

    public static bool IsCompatible(Vehicle vehicle, SpotType spotType, bool allowMotorcycleOverflow)
        => PreferredSpotTypes(vehicle, allowMotorcycleOverflow).Contains(spotType);
}
