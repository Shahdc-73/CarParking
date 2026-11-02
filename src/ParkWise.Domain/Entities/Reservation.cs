using ParkWise.Domain.Common;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Entities;

public class Reservation : Entity
{
    /// <summary>The spot is held until this long after the start time, then released if nobody arrived.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);

    /// <summary>The reserved vehicle may check in this long BEFORE the start time.</summary>
    public static readonly TimeSpan EarlyArrival = TimeSpan.FromMinutes(15);

    // Minimum 30 minutes guarantees the 15 minute hold always ends inside the booking, so the holds of
    // back-to-back reservations on one spot can never overlap.
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxAdvance = TimeSpan.FromDays(30);

    private Reservation() { }

    public Reservation(Guid driverUserId, Guid vehicleId, Guid spotId, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        if (endUtc <= startUtc)
            throw new DomainException("Reservation end must be after its start.");
        if (startUtc < nowUtc)
            throw new DomainException("Reservation cannot start in the past.");
        if (endUtc - startUtc < MinDuration || endUtc - startUtc > MaxDuration)
            throw new DomainException($"A reservation must last between {MinDuration.TotalMinutes:0} minutes and {MaxDuration.TotalHours:0} hours.");
        if (startUtc > nowUtc + MaxAdvance)
            throw new DomainException($"A reservation cannot start more than {MaxAdvance.TotalDays:0} days ahead.");
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

    public DateTime ArrivalWindowStartUtc => StartUtc - EarlyArrival;

    /// <summary>True while the vehicle may claim this reservation at the gate: from 15 min before the start until the hold ends.</summary>
    public bool IsInArrivalWindow(DateTime nowUtc) => IsPending && nowUtc >= ArrivalWindowStartUtc && nowUtc < HoldExpiresAtUtc;

    /// <summary>True while the spot must be kept free for this reservation (start time until the hold ends).</summary>
    public bool IsHoldActive(DateTime nowUtc) => IsPending && nowUtc >= StartUtc && nowUtc < HoldExpiresAtUtc;

    /// <summary>Moves the booking to another spot (used when its spot is taken or out of service when the hold begins).</summary>
    public void Reassign(Guid spotId)
    {
        if (!IsPending) throw new DomainException($"A {Status} reservation cannot be moved.");
        SpotId = spotId;
    }

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
