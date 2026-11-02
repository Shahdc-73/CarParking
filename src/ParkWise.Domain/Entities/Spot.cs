using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Entities;

public class Spot : Entity
{
    private Spot() { }

    internal Spot(Guid levelId, string code, SpotType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        LevelId = levelId;
        Code = code.Trim().ToUpperInvariant();
        Type = type;
        Status = SpotStatus.Available;
    }

    public Guid LevelId { get; private set; }
    public string Code { get; private set; } = null!;
    public SpotType Type { get; private set; }
    public SpotStatus Status { get; private set; }

    /// <summary>SQL Server rowversion: optimistic concurrency token, changes on every update.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public void Occupy()
    {
        if (Status is not (SpotStatus.Available or SpotStatus.Reserved))
            throw new DomainException($"Spot {Code} cannot be occupied while {Status}.");
        Status = SpotStatus.Occupied;
    }

    public void Reserve()
    {
        if (Status != SpotStatus.Available)
            throw new DomainException($"Spot {Code} cannot be reserved while {Status}.");
        Status = SpotStatus.Reserved;
    }

    public void Release()
    {
        if (Status is not (SpotStatus.Occupied or SpotStatus.Reserved))
            throw new DomainException($"Spot {Code} is not in use ({Status}).");
        Status = SpotStatus.Available;
    }

    public void SetOutOfService()
    {
        if (Status is SpotStatus.Occupied or SpotStatus.Reserved)
            throw new DomainException($"Spot {Code} is {Status.ToString().ToLowerInvariant()} and cannot be taken out of service.");
        Status = SpotStatus.OutOfService;
    }

    public void ReturnToService()
    {
        if (Status != SpotStatus.OutOfService)
            throw new DomainException($"Spot {Code} is not out of service.");
        Status = SpotStatus.Available;
    }

    public void Rename(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code.Trim().ToUpperInvariant();
    }

    public void ChangeType(SpotType type)
    {
        if (Status is SpotStatus.Occupied or SpotStatus.Reserved)
            throw new DomainException($"Spot {Code} is in use; its type cannot change.");
        Type = type;
    }
}
