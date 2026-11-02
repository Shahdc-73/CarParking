using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Application.Pricing;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Parking;

public sealed class LotBoardService(IAppDbContext db, IPricingService pricing, ICurrentUser currentUser, TimeProvider clock) : ILotBoardService
{
    public async Task<Result<LotBoardDto>> GetBoardAsync(Guid lotId, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null) return Error.NotFound("lots.not_found", "Lot not found.");

        var now = clock.GetUtcNow().UtcDateTime;
        var isStaff = currentUser.IsStaff();
        var me = currentUser.UserId;

        var levels = await db.Levels.AsNoTracking().Where(l => l.LotId == lotId).OrderBy(l => l.Number)
            .Select(l => new { l.Id, l.Number, l.Name }).ToListAsync(ct);

        var spots = await (from s in db.Spots.AsNoTracking()
                           join l in db.Levels on s.LevelId equals l.Id
                           where l.LotId == lotId
                           orderby s.Code
                           select new { s.Id, s.LevelId, s.Code, s.Type, s.Status }).ToListAsync(ct);

        var spotIds = from s in db.Spots join l in db.Levels on s.LevelId equals l.Id where l.LotId == lotId select s.Id;

        // One query for every parked vehicle in the lot, not one per spot.
        var parked = await (from se in db.Sessions.AsNoTracking()
                            join v in db.Vehicles on se.VehicleId equals v.Id
                            where se.Status == SessionStatus.Active && spotIds.Contains(se.SpotId)
                            select new { se.Id, se.SpotId, se.CheckInAtUtc, v.PlateNumber, v.OwnerUserId })
            .ToDictionaryAsync(x => x.SpotId, ct);

        // Reservation holds are internal gate information: staff only.
        var holdFloor = now - Reservation.HoldDuration;
        var holds = isStaff
            ? await db.Reservations.AsNoTracking()
                .Where(r => r.Status == ReservationStatus.Pending && r.StartUtc <= now && r.StartUtc > holdFloor && spotIds.Contains(r.SpotId))
                .Select(r => new { r.SpotId, r.StartUtc })
                .ToDictionaryAsync(r => r.SpotId, r => r.StartUtc + Reservation.HoldDuration, ct)
            : [];

        // Price terms once per spot type that actually has a parked car.
        var terms = new Dictionary<SpotType, PricingTerms?>();
        foreach (var type in spots.Where(s => parked.ContainsKey(s.Id)).Select(s => s.Type).Distinct())
        {
            var result = await pricing.GetTermsAsync(type, now, ct);
            terms[type] = result.IsSuccess ? result.Value : null;
        }

        BoardSpotDto ToBoardSpot(Guid id, string code, SpotType type, SpotStatus status)
        {
            SpotOccupancyDto? occupancy = null;
            if (status == SpotStatus.Occupied && parked.TryGetValue(id, out var p))
            {
                var mine = me is not null && p.OwnerUserId == me;
                // Other people's cars are shown as "occupied" and nothing more: no plate, no times, no fee.
                if (isStaff || mine)
                {
                    var t = terms.GetValueOrDefault(type);
                    var fee = t is null ? (decimal?)null : FeeCalculator.Calculate(p.CheckInAtUtc, now, t).Total;
                    occupancy = new SpotOccupancyDto(p.Id, p.CheckInAtUtc, p.PlateNumber, fee, t?.FreeMinutes, mine);
                }
            }

            DateTime? holdUntil = status == SpotStatus.Reserved && holds.TryGetValue(id, out var until) ? until : null;
            return new BoardSpotDto(id, code, type, status, occupancy, holdUntil);
        }

        var levelDtos = levels.Select(l => new BoardLevelDto(l.Id, l.Number, l.Name,
            spots.Where(s => s.LevelId == l.Id).Select(s => ToBoardSpot(s.Id, s.Code, s.Type, s.Status)).ToList())).ToList();

        var counts = spots.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count());
        int Of(SpotStatus s) => counts.GetValueOrDefault(s);
        var totals = new AvailabilityCounts(spots.Count, Of(SpotStatus.Available), Of(SpotStatus.Occupied), Of(SpotStatus.Reserved), Of(SpotStatus.OutOfService));

        return new LotBoardDto(lot.Id, lot.Name, lot.IsActive, now, totals, levelDtos);
    }
}
