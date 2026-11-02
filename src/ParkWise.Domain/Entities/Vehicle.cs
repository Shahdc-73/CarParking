using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;

namespace ParkWise.Domain.Entities;

public class Vehicle : Entity
{
    private Vehicle() { }

    public Vehicle(string plateNumber, VehicleType type, string color, Guid? ownerUserId = null, bool hasDisabledPermit = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        PlateNumber = NormalizePlate(plateNumber);
        Type = type;
        Color = color.Trim();
        OwnerUserId = ownerUserId;
        HasDisabledPermit = hasDisabledPermit;
    }

    public string PlateNumber { get; private set; } = null!;
    public VehicleType Type { get; private set; }
    public string Color { get; private set; } = null!;

    /// <summary>Null for walk-in vehicles checked in by an attendant without a registered driver.</summary>
    public Guid? OwnerUserId { get; private set; }

    public bool HasDisabledPermit { get; private set; }

    /// <summary>"ab-123 c" and "AB123C" must be the same vehicle, so the unique index is on the normalized form.</summary>
    public static string NormalizePlate(string plate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plate);
        var normalized = new string(plate.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length is < 2 or > 10)
            throw new Exceptions.DomainException("Plate number must be 2-10 letters/digits.");
        return normalized;
    }

    public void Update(VehicleType type, string color, bool hasDisabledPermit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        Type = type;
        Color = color.Trim();
        HasDisabledPermit = hasDisabledPermit;
    }
}
