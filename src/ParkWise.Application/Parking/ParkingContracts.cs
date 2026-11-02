using ParkWise.Application.Common;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Parking;

// ---- Lots & levels ----
public sealed record LotDto(Guid Id, string Name, string Address, bool IsActive, bool AllowMotorcycleOverflow, int LevelCount, int SpotCount);
public sealed record LevelDto(Guid Id, Guid LotId, int Number, string Name, int SpotCount);
public sealed record LotDetailDto(Guid Id, string Name, string Address, bool IsActive, bool AllowMotorcycleOverflow, IReadOnlyList<LevelDto> Levels);

public sealed record SaveLotRequest(string Name, string Address, bool AllowMotorcycleOverflow);
public sealed record CreateLevelRequest(int Number, string Name);
public sealed record UpdateLevelRequest(string Name);

public sealed record LotListQuery(string? Search, bool? IsActive, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

// ---- Spots ----
public sealed record SpotDto(Guid Id, Guid LotId, Guid LevelId, int LevelNumber, string Code, SpotType Type, SpotStatus Status);

public sealed record CreateSpotRequest(string Code, SpotType Type);
public sealed record UpdateSpotRequest(string Code, SpotType Type);
public sealed record SetSpotServiceRequest(bool InService);

public sealed record SpotListQuery(Guid? LevelId, SpotType? Type, SpotStatus? Status, string? Search, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

// ---- Availability ----
public sealed record AvailabilityCounts(int Total, int Available, int Occupied, int Reserved, int OutOfService);
public sealed record TypeAvailability(SpotType Type, AvailabilityCounts Counts);
public sealed record LevelAvailability(Guid LevelId, int Number, string Name, AvailabilityCounts Counts, IReadOnlyList<TypeAvailability> ByType);
public sealed record LotAvailabilityDto(Guid LotId, string LotName, AvailabilityCounts Totals, IReadOnlyList<TypeAvailability> ByType, IReadOnlyList<LevelAvailability> ByLevel);

// ---- Services ----
public interface ILotService
{
    Task<PagedResult<LotDto>> ListAsync(LotListQuery query, CancellationToken ct);
    Task<Result<LotDetailDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<LotDto>> CreateAsync(SaveLotRequest request, CancellationToken ct);
    Task<Result<LotDto>> UpdateAsync(Guid id, SaveLotRequest request, CancellationToken ct);
    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);

    Task<Result<LevelDto>> AddLevelAsync(Guid lotId, CreateLevelRequest request, CancellationToken ct);
    Task<Result<LevelDto>> UpdateLevelAsync(Guid levelId, UpdateLevelRequest request, CancellationToken ct);
    Task<Result> DeleteLevelAsync(Guid levelId, CancellationToken ct);

    Task<Result<LotAvailabilityDto>> GetAvailabilityAsync(Guid lotId, CancellationToken ct);
}

public interface ISpotService
{
    Task<Result<PagedResult<SpotDto>>> ListAsync(Guid lotId, SpotListQuery query, CancellationToken ct);
    Task<Result<SpotDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<SpotDto>> CreateAsync(Guid levelId, CreateSpotRequest request, CancellationToken ct);
    Task<Result<SpotDto>> UpdateAsync(Guid id, UpdateSpotRequest request, CancellationToken ct);
    Task<Result<SpotDto>> SetServiceAsync(Guid id, bool inService, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}
