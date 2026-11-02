using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Parking;

public sealed class SpotService(IAppDbContext db, IAuditLogger audit) : ISpotService
{
    private const string SpotEntity = "Spot";
    private static readonly Error SpotNotFound = Error.NotFound("spots.not_found", "Spot not found.");

    public async Task<Result<PagedResult<SpotDto>>> ListAsync(Guid lotId, SpotListQuery query, CancellationToken ct)
    {
        if (!await db.Lots.AnyAsync(l => l.Id == lotId, ct))
            return Error.NotFound("lots.not_found", "Lot not found.");

        var q = from s in db.Spots.AsNoTracking()
                join l in db.Levels on s.LevelId equals l.Id
                where l.LotId == lotId
                select new { Spot = s, LevelNumber = l.Number };

        if (query.LevelId is { } levelId) q = q.Where(x => x.Spot.LevelId == levelId);
        if (query.Type is { } type) q = q.Where(x => x.Spot.Type == type);
        if (query.Status is { } status) q = q.Where(x => x.Spot.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(x => x.Spot.Code.Contains(term));
        }

        q = query.Sort?.ToLowerInvariant() switch
        {
            "code_desc" => q.OrderByDescending(x => x.Spot.Code),
            "type" => q.OrderBy(x => x.Spot.Type).ThenBy(x => x.Spot.Code),
            "status" => q.OrderBy(x => x.Spot.Status).ThenBy(x => x.Spot.Code),
            _ => q.OrderBy(x => x.LevelNumber).ThenBy(x => x.Spot.Code),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var items = await q.Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(x => new SpotDto(x.Spot.Id, lotId, x.Spot.LevelId, x.LevelNumber, x.Spot.Code, x.Spot.Type, x.Spot.Status))
            .ToListAsync(ct);

        return new PagedResult<SpotDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<SpotDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await ProjectAsync(id, ct);
        return dto is null ? SpotNotFound : dto;
    }

    public async Task<Result<SpotDto>> CreateAsync(Guid levelId, CreateSpotRequest request, CancellationToken ct)
    {
        var level = await db.Levels.Include(l => l.Spots).SingleOrDefaultAsync(l => l.Id == levelId, ct);
        if (level is null) return Error.NotFound("levels.not_found", "Level not found.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (level.Spots.Any(s => s.Code == code))
            return Error.Conflict("spots.code_taken", $"Spot '{code}' already exists on this level.");

        var spot = level.AddSpot(request.Code, request.Type);
        audit.Log("Spot.Created", SpotEntity, spot.Id, after: new { levelId, spot.Code, spot.Type });
        await db.SaveChangesAsync(ct);

        return (await ProjectAsync(spot.Id, ct))!;
    }

    public async Task<Result<SpotDto>> UpdateAsync(Guid id, UpdateSpotRequest request, CancellationToken ct)
    {
        var spot = await db.Spots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (spot is null) return SpotNotFound;

        var code = request.Code.Trim().ToUpperInvariant();
        if (code != spot.Code && await db.Spots.AnyAsync(s => s.LevelId == spot.LevelId && s.Code == code && s.Id != id, ct))
            return Error.Conflict("spots.code_taken", $"Spot '{code}' already exists on this level.");

        if (request.Type != spot.Type)
        {
            // Decision 3: no type change while the spot is in use (the domain checks Occupied/Reserved),
            // nor while a pending reservation was made for the old type.
            if (await db.Reservations.AnyAsync(r => r.SpotId == id && r.Status == ReservationStatus.Pending, ct))
                return Error.Conflict("spots.has_pending_reservations", "This spot has pending reservations; its type cannot change.");
        }

        var before = new { spot.Code, spot.Type };
        if (request.Type != spot.Type) spot.ChangeType(request.Type); // throws DomainException (409) if in use
        spot.Rename(request.Code);
        audit.Log("Spot.Updated", SpotEntity, spot.Id, before, new { spot.Code, spot.Type });
        await db.SaveChangesAsync(ct);

        return (await ProjectAsync(id, ct))!;
    }

    public async Task<Result<SpotDto>> SetServiceAsync(Guid id, bool inService, CancellationToken ct)
    {
        var spot = await db.Spots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (spot is null) return SpotNotFound;

        var isOutOfService = spot.Status == SpotStatus.OutOfService;
        if (inService == !isOutOfService) return (await ProjectAsync(id, ct))!; // already in the requested state

        var before = spot.Status;
        if (inService) spot.ReturnToService(); else spot.SetOutOfService(); // DomainException (409) if occupied/reserved
        audit.Log(inService ? "Spot.ReturnedToService" : "Spot.TakenOutOfService", SpotEntity, spot.Id,
            new { Status = before }, new { spot.Status });

        // The spot's rowversion makes this safe against a concurrent check-in: whoever saves second gets a 409.
        await db.SaveChangesAsync(ct);
        return (await ProjectAsync(id, ct))!;
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var spot = await db.Spots.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (spot is null) return Result.Failure(SpotNotFound);

        if (await db.Sessions.AnyAsync(s => s.SpotId == id, ct) || await db.Reservations.AnyAsync(r => r.SpotId == id, ct))
            return Result.Failure(Error.Conflict("spots.has_history", "This spot has parking history and cannot be deleted. Take it out of service instead."));

        audit.Log("Spot.Deleted", SpotEntity, spot.Id, before: new { spot.LevelId, spot.Code, spot.Type });
        db.Spots.Remove(spot);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private Task<SpotDto?> ProjectAsync(Guid id, CancellationToken ct)
        => (from s in db.Spots.AsNoTracking()
            join l in db.Levels on s.LevelId equals l.Id
            where s.Id == id
            select new SpotDto(s.Id, l.LotId, s.LevelId, l.Number, s.Code, s.Type, s.Status))
            .SingleOrDefaultAsync(ct);
}
