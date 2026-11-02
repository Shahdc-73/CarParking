using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Parking;

public sealed class LotService(IAppDbContext db, IAuditLogger audit) : ILotService
{
    private const string LotEntity = "ParkingLot";
    private const string LevelEntity = "Level";

    public async Task<PagedResult<LotDto>> ListAsync(LotListQuery query, CancellationToken ct)
    {
        var q = db.Lots.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(l => l.Name.Contains(term) || l.Address.Contains(term));
        }
        if (query.IsActive is { } active) q = q.Where(l => l.IsActive == active);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "name_desc" => q.OrderByDescending(l => l.Name),
            _ => q.OrderBy(l => l.Name),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var items = await q.Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(l => new LotDto(l.Id, l.Name, l.Address, l.IsActive, l.AllowMotorcycleOverflow,
                l.Levels.Count(), l.Levels.SelectMany(v => v.Spots).Count()))
            .ToListAsync(ct);

        return new PagedResult<LotDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<LotDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
        if (lot is null) return LotNotFound;

        var levels = await db.Levels.AsNoTracking()
            .Where(l => l.LotId == id)
            .OrderBy(l => l.Number)
            .Select(l => new LevelDto(l.Id, l.LotId, l.Number, l.Name, l.Spots.Count()))
            .ToListAsync(ct);

        return new LotDetailDto(lot.Id, lot.Name, lot.Address, lot.IsActive, lot.AllowMotorcycleOverflow, levels);
    }

    public async Task<Result<LotDto>> CreateAsync(SaveLotRequest request, CancellationToken ct)
    {
        if (await NameTakenAsync(request.Name, excludeId: null, ct))
            return Error.Conflict("lots.name_taken", "A lot with this name already exists.");

        var lot = new ParkingLot(request.Name, request.Address, request.AllowMotorcycleOverflow);
        db.Lots.Add(lot);
        audit.Log("Lot.Created", LotEntity, lot.Id, after: Snapshot(lot));
        await db.SaveChangesAsync(ct); // lot + audit entry commit together

        return new LotDto(lot.Id, lot.Name, lot.Address, lot.IsActive, lot.AllowMotorcycleOverflow, 0, 0);
    }

    public async Task<Result<LotDto>> UpdateAsync(Guid id, SaveLotRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (lot is null) return LotNotFound;

        if (await NameTakenAsync(request.Name, excludeId: id, ct))
            return Error.Conflict("lots.name_taken", "A lot with this name already exists.");

        var before = Snapshot(lot);
        lot.Update(request.Name, request.Address, request.AllowMotorcycleOverflow);
        audit.Log("Lot.Updated", LotEntity, lot.Id, before, Snapshot(lot));
        await db.SaveChangesAsync(ct);

        return await CountsFor(lot, ct);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var lot = await db.Lots.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (lot is null) return Result.Failure(LotNotFound);
        if (lot.IsActive == isActive) return Result.Success(); // idempotent

        if (!isActive)
        {
            var spotIds = SpotIdsInLot(id);
            if (await db.Sessions.AnyAsync(s => s.Status == SessionStatus.Active && spotIds.Contains(s.SpotId), ct))
                return Result.Failure(Error.Conflict("lots.has_active_sessions", "Cannot deactivate a lot while vehicles are parked in it."));
            if (await db.Reservations.AnyAsync(r => r.Status == ReservationStatus.Pending && spotIds.Contains(r.SpotId), ct))
                return Result.Failure(Error.Conflict("lots.has_pending_reservations", "Cannot deactivate a lot that has pending reservations."));
        }

        var before = Snapshot(lot);
        if (isActive) lot.Activate(); else lot.Deactivate();
        audit.Log(isActive ? "Lot.Activated" : "Lot.Deactivated", LotEntity, lot.Id, before, Snapshot(lot));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var lot = await db.Lots.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (lot is null) return Result.Failure(LotNotFound);

        var spotIds = SpotIdsInLot(id);
        if (await HasHistoryAsync(spotIds, ct))
            return Result.Failure(Error.Conflict("lots.has_history", "This lot has parking history and cannot be deleted. Deactivate it instead."));

        audit.Log("Lot.Deleted", LotEntity, lot.Id, before: Snapshot(lot));
        db.Lots.Remove(lot); // levels and spots go with it (cascade delete in the database)
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<LevelDto>> AddLevelAsync(Guid lotId, CreateLevelRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.Include(l => l.Levels).SingleOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null) return LotNotFound;

        if (lot.Levels.Any(l => l.Number == request.Number))
            return Error.Conflict("levels.number_taken", $"Level {request.Number} already exists in this lot.");

        var level = lot.AddLevel(request.Number, request.Name);
        audit.Log("Level.Created", LevelEntity, level.Id, after: new { lotId, level.Number, level.Name });
        await db.SaveChangesAsync(ct);

        return new LevelDto(level.Id, lotId, level.Number, level.Name, 0);
    }

    public async Task<Result<LevelDto>> UpdateLevelAsync(Guid levelId, UpdateLevelRequest request, CancellationToken ct)
    {
        var level = await db.Levels.SingleOrDefaultAsync(l => l.Id == levelId, ct);
        if (level is null) return LevelNotFound;

        var before = new { level.Number, level.Name };
        level.Rename(request.Name);
        audit.Log("Level.Updated", LevelEntity, level.Id, before, new { level.Number, level.Name });
        await db.SaveChangesAsync(ct);

        var spotCount = await db.Spots.CountAsync(s => s.LevelId == levelId, ct);
        return new LevelDto(level.Id, level.LotId, level.Number, level.Name, spotCount);
    }

    public async Task<Result> DeleteLevelAsync(Guid levelId, CancellationToken ct)
    {
        var level = await db.Levels.SingleOrDefaultAsync(l => l.Id == levelId, ct);
        if (level is null) return Result.Failure(LevelNotFound);

        var spotIds = db.Spots.Where(s => s.LevelId == levelId).Select(s => s.Id);
        if (await HasHistoryAsync(spotIds, ct))
            return Result.Failure(Error.Conflict("levels.has_history", "This level has parking history and cannot be deleted."));

        audit.Log("Level.Deleted", LevelEntity, level.Id, before: new { level.LotId, level.Number, level.Name });
        db.Levels.Remove(level);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<LotAvailabilityDto>> GetAvailabilityAsync(Guid lotId, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null) return Error.NotFound("lots.not_found", "Lot not found.");

        // Group in the database: the result is tiny (levels x types x statuses) no matter how many spots exist.
        var rows = await (from s in db.Spots.AsNoTracking()
                          join l in db.Levels on s.LevelId equals l.Id
                          where l.LotId == lotId
                          group s by new { l.Id, l.Number, l.Name, s.Type, s.Status } into g
                          select new { g.Key.Id, g.Key.Number, g.Key.Name, g.Key.Type, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        // Levels that have no spots yet still appear (with zero counts).
        var levels = await db.Levels.AsNoTracking().Where(l => l.LotId == lotId).OrderBy(l => l.Number)
            .Select(l => new { l.Id, l.Number, l.Name }).ToListAsync(ct);

        static AvailabilityCounts Sum(IEnumerable<(SpotStatus Status, int Count)> items)
        {
            var list = items.ToList();
            int Of(SpotStatus s) => list.Where(i => i.Status == s).Sum(i => i.Count);
            return new AvailabilityCounts(list.Sum(i => i.Count), Of(SpotStatus.Available), Of(SpotStatus.Occupied),
                Of(SpotStatus.Reserved), Of(SpotStatus.OutOfService));
        }

        static List<TypeAvailability> ByType(IEnumerable<(SpotType Type, SpotStatus Status, int Count)> items)
            => items.GroupBy(i => i.Type).OrderBy(g => g.Key)
                .Select(g => new TypeAvailability(g.Key, Sum(g.Select(i => (i.Status, i.Count))))).ToList();

        var all = rows.Select(r => (r.Type, r.Status, r.Count)).ToList();
        var byLevel = levels.Select(l =>
        {
            var mine = rows.Where(r => r.Id == l.Id).Select(r => (r.Type, r.Status, r.Count)).ToList();
            return new LevelAvailability(l.Id, l.Number, l.Name, Sum(mine.Select(i => (i.Status, i.Count))), ByType(mine));
        }).ToList();

        return new LotAvailabilityDto(lot.Id, lot.Name, Sum(all.Select(i => (i.Status, i.Count))), ByType(all), byLevel);
    }

    // ---- helpers ----
    private static readonly Error LotNotFound = Error.NotFound("lots.not_found", "Lot not found.");
    private static readonly Error LevelNotFound = Error.NotFound("levels.not_found", "Level not found.");

    private static object Snapshot(ParkingLot l) => new { l.Name, l.Address, l.IsActive, l.AllowMotorcycleOverflow };

    private Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        return db.Lots.AnyAsync(l => l.Name == trimmed && (excludeId == null || l.Id != excludeId), ct);
    }

    private IQueryable<Guid> SpotIdsInLot(Guid lotId)
        => from s in db.Spots join l in db.Levels on s.LevelId equals l.Id where l.LotId == lotId select s.Id;

    private async Task<bool> HasHistoryAsync(IQueryable<Guid> spotIds, CancellationToken ct)
        => await db.Sessions.AnyAsync(s => spotIds.Contains(s.SpotId), ct)
           || await db.Reservations.AnyAsync(r => spotIds.Contains(r.SpotId), ct);

    private async Task<LotDto> CountsFor(ParkingLot lot, CancellationToken ct)
    {
        var levels = await db.Levels.CountAsync(l => l.LotId == lot.Id, ct);
        var spots = await db.Spots.CountAsync(s => db.Levels.Any(l => l.Id == s.LevelId && l.LotId == lot.Id), ct);
        return new LotDto(lot.Id, lot.Name, lot.Address, lot.IsActive, lot.AllowMotorcycleOverflow, levels, spots);
    }
}
