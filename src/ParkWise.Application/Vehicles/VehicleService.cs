using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;

namespace ParkWise.Application.Vehicles;

public sealed class VehicleService(IAppDbContext db, ICurrentUser currentUser) : IVehicleService
{
    // Someone else's vehicle is reported as "not found", not "forbidden", so ids and plates cannot be probed.
    private static readonly Error NotFound = Error.NotFound("vehicles.not_found", "Vehicle not found.");

    public async Task<PagedResult<VehicleDto>> ListAsync(VehicleListQuery query, CancellationToken ct)
    {
        var q = VisibleVehicles();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = new string(query.Search.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            q = q.Where(v => v.PlateNumber.Contains(term));
        }
        if (query.Type is { } type) q = q.Where(v => v.Type == type);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "plate_desc" => q.OrderByDescending(v => v.PlateNumber),
            "type" => q.OrderBy(v => v.Type).ThenBy(v => v.PlateNumber),
            _ => q.OrderBy(v => v.PlateNumber),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var items = await q.Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(v => new VehicleDto(v.Id, v.PlateNumber, v.Type, v.Color, v.HasDisabledPermit, v.OwnerUserId))
            .ToListAsync(ct);

        return new PagedResult<VehicleDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<VehicleDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await VisibleVehicles().SingleOrDefaultAsync(v => v.Id == id, ct);
        return vehicle is null ? NotFound : ToDto(vehicle);
    }

    public async Task<Result<VehicleDto>> LookupByPlateAsync(string plate, CancellationToken ct)
    {
        string normalized;
        try { normalized = Vehicle.NormalizePlate(plate); }
        catch (Exception) { return Error.Validation("vehicles.invalid_plate", "Plate number must be 2-10 letters/digits."); }

        var vehicle = await VisibleVehicles().SingleOrDefaultAsync(v => v.PlateNumber == normalized, ct);
        return vehicle is null ? NotFound : ToDto(vehicle);
    }

    public async Task<Result<VehicleDto>> CreateAsync(CreateVehicleRequest request, CancellationToken ct)
    {
        var plate = Vehicle.NormalizePlate(request.PlateNumber);
        if (await db.Vehicles.AnyAsync(v => v.PlateNumber == plate, ct))
            return Error.Conflict("vehicles.plate_taken", "A vehicle with this plate number is already registered.");

        // Drivers own what they register. Staff register walk-in vehicles (no owner).
        var isStaff = currentUser.IsStaff();
        var owner = isStaff ? (Guid?)null : currentUser.UserId;

        // Disabled-permit status gives access to reserved spots, so only staff (who can check the permit) set it.
        var vehicle = new Vehicle(plate, request.Type, request.Color, owner, hasDisabledPermit: isStaff && request.HasDisabledPermit);
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct); // a concurrent duplicate hits the unique index and becomes a 409

        return ToDto(vehicle);
    }

    public async Task<Result<VehicleDto>> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await VisibleVehicles(tracked: true).SingleOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return NotFound;

        var permit = currentUser.IsStaff() ? request.HasDisabledPermit : vehicle.HasDisabledPermit;
        vehicle.Update(request.Type, request.Color, permit);
        await db.SaveChangesAsync(ct);

        return ToDto(vehicle);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await VisibleVehicles(tracked: true).SingleOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return Result.Failure(NotFound);

        if (await db.Sessions.AnyAsync(s => s.VehicleId == id, ct) || await db.Reservations.AnyAsync(r => r.VehicleId == id, ct))
            return Result.Failure(Error.Conflict("vehicles.has_history", "This vehicle has parking history and cannot be deleted."));

        db.Vehicles.Remove(vehicle);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>The ownership rule lives in ONE place: staff see everything, everyone else only their own vehicles.</summary>
    private IQueryable<Vehicle> VisibleVehicles(bool tracked = false)
    {
        var q = tracked ? db.Vehicles.AsQueryable() : db.Vehicles.AsNoTracking();
        if (currentUser.IsStaff()) return q;

        var me = currentUser.UserId;
        return q.Where(v => v.OwnerUserId != null && v.OwnerUserId == me);
    }

    private static VehicleDto ToDto(Vehicle v) => new(v.Id, v.PlateNumber, v.Type, v.Color, v.HasDisabledPermit, v.OwnerUserId);
}
