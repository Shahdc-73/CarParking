using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Entities;

public class Reservation : Entity
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);

    private Reservation() { }

    public Reservation(Guid driverUserId, Guid vehicleId, Guid spotId, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        if (endUtc <= startUtc)
            throw new DomainException("Reservation end must be after its start.");
        if (startUtc < nowUtc)
            throw new DomainException("Reservation cannot start in the past.");
        DriverUserId = driverUserId;
        VehicleId = vehicleId;
        SpotId = spotId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Status = ReservationStatus.Pending;
    }

    public Guid DriverUserId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid SpotId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public ReservationStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>The spot is held until 15 minutes after the start time.</summary>
    public DateTime HoldExpiresAtUtc => StartUtc + HoldDuration;

    public bool IsPending => Status == ReservationStatus.Pending;

    /// <summary>Half-open interval overlap: back-to-back bookings (end == other start) do not conflict.</summary>
    public static bool Overlaps(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
        => startA < endB && startB < endA;

    public void Cancel()
    {
        if (!IsPending) throw new DomainException($"A {Status} reservation cannot be cancelled.");
        Status = ReservationStatus.Cancelled;
    }

    public void Fulfill()
    {
        if (!IsPending) throw new DomainException($"A {Status} reservation cannot be fulfilled.");
        Status = ReservationStatus.Fulfilled;
    }

    /// <returns>true if it expired now; false if still within its hold window or not pending.</returns>
    public bool TryExpire(DateTime nowUtc)
    {
        if (!IsPending || nowUtc < HoldExpiresAtUtc) return false;
        Status = ReservationStatus.Expired;
        return true;
    }
}
