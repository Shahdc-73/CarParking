using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Reservations;

/// <summary>
/// What the background job does each tick. It is state-based and idempotent: it looks at what is true NOW and fixes it,
/// so it does not matter if a tick is late, skipped, or two API instances run it at the same time.
/// </summary>
public sealed class ReservationMaintenance(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : IReservationMaintenance
{
    private const string Entity = nameof(Reservation);
    private const int BatchSize = 200;

    public async Task<MaintenanceResult> RunOnceAsync(CancellationToken ct)
    {
        var expired = await ExpireAsync(ct);
        var (started, reassigned, cancelled) = await StartHoldsAsync(ct);
        return new MaintenanceResult(expired, started, reassigned, cancelled);
    }

    /// <summary>A pending reservation whose hold has ended (start + 15 min) is released: Expired, and its spot is freed.</summary>
    private async Task<int> ExpireAsync(CancellationToken ct)
    {
        var now = Now();
        var cutoff = now - Reservation.HoldDuration;
        var count = 0;

        var due = await db.Reservations.Where(r => r.Status == ReservationStatus.Pending && r.StartUtc <= cutoff)
            .OrderBy(r => r.StartUtc).Take(BatchSize).Select(r => r.Id).ToListAsync(ct);

        foreach (var id in due)
        {
            // One unit of work per reservation: a conflict on one spot never blocks the others, and the next tick retries it.
            if (await TryAsync(async () =>
            {
                var reservation = await db.Reservations.SingleAsync(r => r.Id == id, ct);
                if (!reservation.TryExpire(now)) return false;

                var spot = await db.Spots.SingleAsync(s => s.Id == reservation.SpotId, ct);
                if (spot.Status == SpotStatus.Reserved) spot.Release();

                audit.Log("Reservation.Expired", Entity, reservation.Id,
                    before: new { Status = ReservationStatus.Pending }, after: new { reservation.Status, reservation.HoldExpiresAtUtc });
                await db.SaveChangesAsync(ct);
                return true;
            })) count++;
        }

        return count;
    }

    /// <summary>
    /// From its start time a reservation must physically hold its spot. If the spot is free it becomes Reserved. If someone
    /// is still parked there (or it was taken out of service) the booking moves to another free spot of the same type,
    /// or is cancelled when there is none.
    /// </summary>
    private async Task<(int Started, int Reassigned, int Cancelled)> StartHoldsAsync(CancellationToken ct)
    {
        var now = Now();
        var holdFloor = now - Reservation.HoldDuration;
        int started = 0, reassigned = 0, cancelled = 0;

        var due = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Pending && r.StartUtc <= now && r.StartUtc > holdFloor)
            .OrderBy(r => r.StartUtc).Take(BatchSize).Select(r => r.Id).ToListAsync(ct);

        foreach (var id in due)
        {
            await TryAsync(async () =>
            {
                var reservation = await db.Reservations.SingleAsync(r => r.Id == id, ct);
                if (!reservation.IsHoldActive(now)) return false;

                var spot = await db.Spots.SingleAsync(s => s.Id == reservation.SpotId, ct);
                if (spot.Status == SpotStatus.Reserved) return false;       // already held
                if (spot.Status == SpotStatus.Available)
                {
                    spot.Reserve();
                    await db.SaveChangesAsync(ct);
                    started++;
                    return true;
                }

                // Occupied or out of service: find a replacement in the same lot.
                var lotId = await db.Levels.Where(l => l.Id == spot.LevelId).Select(l => l.LotId).SingleAsync(ct);
                var replacement = await ReservationSpots.Free(db, lotId, spot.Type, reservation.StartUtc, reservation.EndUtc,
                    excludeReservationId: reservation.Id, availableNowOnly: true).FirstOrDefaultAsync(ct);

                if (replacement is not null)
                {
                    var oldSpotId = spot.Id;
                    reservation.Reassign(replacement.Id);
                    replacement.Reserve();
                    audit.Log("Reservation.Reassigned", Entity, reservation.Id,
                        before: new { SpotId = oldSpotId, spot.Code, Reason = spot.Status }, after: new { SpotId = replacement.Id, replacement.Code });
                    await db.SaveChangesAsync(ct);
                    reassigned++;
                }
                else
                {
                    reservation.Cancel();
                    audit.Log("Reservation.CancelledNoSpot", Entity, reservation.Id,
                        before: new { SpotId = spot.Id, spot.Code, Reason = spot.Status }, after: new { reservation.Status });
                    await db.SaveChangesAsync(ct);
                    cancelled++;
                }

                return true;
            });
        }

        return (started, reassigned, cancelled);
    }

    /// <summary>Runs one unit of work; if it loses a race (another request or instance changed the same rows) it is skipped and retried next tick.</summary>
    private async Task<bool> TryAsync(Func<Task<bool>> work)
    {
        try
        {
            return await work();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
