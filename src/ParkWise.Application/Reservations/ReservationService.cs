using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Reservations;

public sealed class ReservationService(IAppDbContext db, IAuditLogger audit, ICurrentUser currentUser, IAvailabilityNotifier notifier, TimeProvider clock)
    : IReservationService
{
    private const string Entity = nameof(Reservation);
    private const int MaxAttempts = 4;

    private static readonly Error NotFound = Error.NotFound("reservations.not_found", "Reservation not found.");

    public async Task<Result<ReservationDto>> CreateAsync(CreateReservationRequest request, CancellationToken ct)
    {
        // Two people can book the last spot for overlapping windows at the same time. Each booking "touches" the spot
        // row (see CreateOnceAsync), so the database lets only one commit; the loser retries and sees the winner.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreateOnceAsync(request, ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (attempt >= MaxAttempts)
                    return Error.Conflict("reservations.contention", "Reservations are busy right now. Please try again.");
            }
        }
    }

    private async Task<Result<ReservationDto>> CreateOnceAsync(CreateReservationRequest request, CancellationToken ct)
    {
        var now = Now();
        var start = ToUtc(request.StartUtc);
        var end = ToUtc(request.EndUtc);

        if (start < now) return Error.Validation("reservations.start_in_past", "A reservation cannot start in the past.");
        if (start > now + Reservation.MaxAdvance)
            return Error.Validation("reservations.too_far_ahead", $"A reservation cannot start more than {Reservation.MaxAdvance.TotalDays:0} days ahead.");

        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null) return Error.NotFound("lots.not_found", "Lot not found.");
        if (!lot.IsActive) return Error.Conflict("lots.inactive", "This lot is closed.");

        // You can only book for a vehicle you may see: your own, or any vehicle if you are staff.
        var me = currentUser.UserId;
        var vehicle = await db.Vehicles
            .Where(v => v.Id == request.VehicleId && (currentUser.IsStaff() || (v.OwnerUserId != null && v.OwnerUserId == me)))
            .SingleOrDefaultAsync(ct);
        if (vehicle is null) return Error.NotFound("vehicles.not_found", "Vehicle not found.");

        if (!SpotCompatibility.IsCompatible(vehicle, request.SpotType, lot.AllowMotorcycleOverflow))
            return Error.Conflict("reservations.incompatible_spot_type", $"This vehicle cannot use {request.SpotType} spots.");

        if (await db.Reservations.AnyAsync(r => r.VehicleId == vehicle.Id && r.Status == ReservationStatus.Pending
                                                && r.StartUtc < end && start < r.EndUtc, ct))
            return Error.Conflict("reservations.vehicle_overlap", "This vehicle already has a reservation that overlaps this time.");

        var spot = await ReservationSpots.Free(db, lot.Id, request.SpotType, start, end).FirstOrDefaultAsync(ct);
        if (spot is null)
            return Error.Conflict("reservations.no_spot_available", $"No {request.SpotType} spot is free for that time.");

        var reservation = new Reservation(CurrentUserId(), vehicle.Id, spot.Id, start, end, now);
        db.Reservations.Add(reservation);

        // THE CONCURRENCY GUARD. A unique index cannot express "time ranges must not overlap", so instead every booking
        // forces an UPDATE of the spot row. The update is conditional on the spot's rowversion, which every other
        // booking of this spot also read. The first to commit changes the rowversion; the second's UPDATE matches
        // 0 rows and fails, and that request retries and now sees the first booking.
        db.Entry(spot).Property(s => s.Code).IsModified = true;

        // Same trick for the vehicle: two simultaneous bookings of ONE vehicle may pick different spots, so the spot
        // guard alone would let both through. Touching the vehicle row serializes them per vehicle.
        db.Entry(vehicle).Property(v => v.Color).IsModified = true;

        audit.Log("Reservation.Created", Entity, reservation.Id, after: new
        {
            LotId = lot.Id, SpotId = spot.Id, SpotCode = spot.Code, VehicleId = vehicle.Id, vehicle.PlateNumber,
            reservation.StartUtc, reservation.EndUtc,
        });
        await db.SaveChangesAsync(ct);
        await notifier.LotChangedAsync(lot.Id, ct);

        return (await GetAsync(reservation.Id, ct)).Value;
    }

    public async Task<PagedResult<ReservationDto>> ListAsync(ReservationListQuery query, CancellationToken ct)
    {
        var q = Rows();

        if (query.Status is { } status) q = q.Where(r => r.Reservation.Status == status);
        if (query.LotId is { } lotId) q = q.Where(r => r.Lot.Id == lotId);
        if (query.VehicleId is { } vehicleId) q = q.Where(r => r.Reservation.VehicleId == vehicleId);
        if (query.FromUtc is { } from) q = q.Where(r => r.Reservation.EndUtc > from);
        if (query.ToUtc is { } to) q = q.Where(r => r.Reservation.StartUtc < to);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "start_desc" => q.OrderByDescending(r => r.Reservation.StartUtc),
            "status" => q.OrderBy(r => r.Reservation.Status).ThenBy(r => r.Reservation.StartUtc),
            _ => q.OrderBy(r => r.Reservation.StartUtc),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);
        return new PagedResult<ReservationDto>(rows.Select(ToDto).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<ReservationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Rows().SingleOrDefaultAsync(r => r.Reservation.Id == id, ct);
        return row is null ? NotFound : ToDto(row);
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var visible = await Rows().AnyAsync(r => r.Reservation.Id == id, ct);
        if (!visible) return Result.Failure(NotFound);

        var reservation = await db.Reservations.SingleAsync(r => r.Id == id, ct);
        if (!reservation.IsPending)
            return Result.Failure(Error.Conflict("reservations.not_pending", $"A {reservation.Status} reservation cannot be cancelled."));

        var now = Now();
        var holdingSpot = reservation.IsHoldActive(now);
        reservation.Cancel();

        Guid? changedLot = null;
        if (holdingSpot)
        {
            // The spot was being held for this booking: give it back right away.
            var spot = await db.Spots.SingleAsync(s => s.Id == reservation.SpotId, ct);
            if (spot.Status == SpotStatus.Reserved)
            {
                spot.Release();
                changedLot = await db.Levels.Where(l => l.Id == spot.LevelId).Select(l => l.LotId).SingleAsync(ct);
            }
        }

        audit.Log("Reservation.Cancelled", Entity, reservation.Id, before: new { Status = ReservationStatus.Pending }, after: new { reservation.Status });
        await db.SaveChangesAsync(ct);
        if (changedLot is { } lotId) await notifier.LotChangedAsync(lotId, ct);
        return Result.Success();
    }

    public async Task<Result<ReservationAvailabilityDto>> GetAvailabilityAsync(ReservationAvailabilityQuery query, CancellationToken ct)
    {
        if (!await db.Lots.AnyAsync(l => l.Id == query.LotId, ct)) return Error.NotFound("lots.not_found", "Lot not found.");

        var start = ToUtc(query.StartUtc);
        var end = ToUtc(query.EndUtc);
        var count = await ReservationSpots.Free(db, query.LotId, query.SpotType, start, end).CountAsync(ct);
        return new ReservationAvailabilityDto(query.LotId, query.SpotType, start, end, count);
    }

    // ---- queries & mapping ----

    private sealed class Row
    {
        public required Reservation Reservation { get; init; }
        public required Vehicle Vehicle { get; init; }
        public required Spot Spot { get; init; }
        public required Level Level { get; init; }
        public required ParkingLot Lot { get; init; }
    }

    /// <summary>Staff see every reservation; everyone else only the ones they booked.</summary>
    private IQueryable<Row> Rows()
    {
        var q = db.Reservations.AsNoTracking().AsQueryable();
        if (!currentUser.IsStaff())
        {
            var me = currentUser.UserId;
            q = q.Where(r => r.DriverUserId == me);
        }

        return (from r in q
                join v in db.Vehicles on r.VehicleId equals v.Id
                join sp in db.Spots on r.SpotId equals sp.Id
                join lv in db.Levels on sp.LevelId equals lv.Id
                join lot in db.Lots on lv.LotId equals lot.Id
                select new Row { Reservation = r, Vehicle = v, Spot = sp, Level = lv, Lot = lot })
            .AsNoTracking();
    }

    private static ReservationDto ToDto(Row r) => new(
        r.Reservation.Id, r.Vehicle.Id, r.Vehicle.PlateNumber,
        r.Lot.Id, r.Lot.Name, r.Level.Number, r.Spot.Id, r.Spot.Code, r.Spot.Type,
        r.Reservation.StartUtc, r.Reservation.EndUtc, r.Reservation.HoldExpiresAtUtc, r.Reservation.Status, r.Reservation.DriverUserId);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private Guid CurrentUserId() => currentUser.UserId ?? throw new InvalidOperationException("Booking requires an authenticated user.");

    /// <summary>JSON dates ending in Z arrive as UTC; anything else is treated as UTC (Local is converted).</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime()
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
