using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;

namespace ParkWise.Domain.Entities;

public class ParkingLot : Entity
{
    private readonly List<Level> _levels = [];

    private ParkingLot() { }

    public ParkingLot(string name, string address, bool allowMotorcycleOverflow = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Name = name.Trim();
        Address = address.Trim();
        AllowMotorcycleOverflow = allowMotorcycleOverflow;
        IsActive = true;
    }

    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public bool IsActive { get; private set; }

    /// <summary>When true, motorcycles may take Compact/Standard spots if no Motorcycle spot is free.</summary>
    public bool AllowMotorcycleOverflow { get; private set; }

    public IReadOnlyCollection<Level> Levels => _levels;

    public Level AddLevel(int number, string name)
    {
        if (_levels.Any(l => l.Number == number))
            throw new Exceptions.DomainException($"Level {number} already exists in this lot.");
        var level = new Level(Id, number, name);
        _levels.Add(level);
        return level;
    }

    public void Update(string name, string address, bool allowMotorcycleOverflow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Name = name.Trim();
        Address = address.Trim();
        AllowMotorcycleOverflow = allowMotorcycleOverflow;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

public class Level : Entity
{
    private readonly List<Spot> _spots = [];

    private Level() { }

    internal Level(Guid lotId, int number, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LotId = lotId;
        Number = number;
        Name = name.Trim();
    }

    public Guid LotId { get; private set; }
    public int Number { get; private set; }
    public string Name { get; private set; } = null!;

    public IReadOnlyCollection<Spot> Spots => _spots;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public Spot AddSpot(string code, SpotType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        code = code.Trim().ToUpperInvariant();
        if (_spots.Any(s => s.Code == code))
            throw new Exceptions.DomainException($"Spot '{code}' already exists on this level.");
        var spot = new Spot(Id, code, type);
        _spots.Add(spot);
        return spot;
    }
}
