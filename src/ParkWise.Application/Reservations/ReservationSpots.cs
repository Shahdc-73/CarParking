using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Reservations;

/// <summary>The single definition of "this spot is free for that window", used by booking, availability and the job.</summary>
internal static class ReservationSpots
{
    /// <param name="excludeReservationId">Ignore this reservation's own booking (when moving it to another spot).</param>
    /// <param name="availableNowOnly">Only spots that are physically free this moment (needed when a hold begins now).</param>
    public static IQueryable<Spot> Free(
        IAppDbContext db, Guid lotId, SpotType type, DateTime start, DateTime end,
        Guid? excludeReservationId = null, bool availableNowOnly = false)
    {
        var q = from s in db.Spots
                join l in db.Levels on s.LevelId equals l.Id
                where l.LotId == lotId
                      && s.Type == type
                      && s.Status != SpotStatus.OutOfService
                      // Half-open interval overlap: [start, end) vs [r.Start, r.End). Back-to-back bookings do not clash.
                      && !db.Reservations.Any(r => r.SpotId == s.Id
                                                   && r.Status == ReservationStatus.Pending
                                                   && (excludeReservationId == null || r.Id != excludeReservationId.Value)
                                                   && r.StartUtc < end && start < r.EndUtc)
                select new { Spot = s, LevelNumber = l.Number };

        if (availableNowOnly) q = q.Where(x => x.Spot.Status == SpotStatus.Available);

        // Prefer spots that are not occupied right now, then lowest level and code.
        return q.OrderBy(x => x.Spot.Status == SpotStatus.Occupied ? 1 : 0)
            .ThenBy(x => x.LevelNumber).ThenBy(x => x.Spot.Code)
            .Select(x => x.Spot);
    }
}
