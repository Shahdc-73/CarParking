using FluentValidation;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Vehicles;

public sealed record VehicleDto(Guid Id, string PlateNumber, VehicleType Type, string Color, bool HasDisabledPermit, Guid? OwnerUserId);

public sealed record CreateVehicleRequest(string PlateNumber, VehicleType Type, string Color, bool HasDisabledPermit = false);
public sealed record UpdateVehicleRequest(VehicleType Type, string Color, bool HasDisabledPermit = false);

public sealed record VehicleListQuery(string? Search, VehicleType? Type, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

public interface IVehicleService
{
    Task<PagedResult<VehicleDto>> ListAsync(VehicleListQuery query, CancellationToken ct);
    Task<Result<VehicleDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<VehicleDto>> LookupByPlateAsync(string plate, CancellationToken ct);
    Task<Result<VehicleDto>> CreateAsync(CreateVehicleRequest request, CancellationToken ct);
    Task<Result<VehicleDto>> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class CreateVehicleRequestValidator : AbstractValidator<CreateVehicleRequest>
{
    public CreateVehicleRequestValidator()
    {
        RuleFor(x => x.PlateNumber).NotEmpty().Must(BeAValidPlate).WithMessage("Plate number must be 2-10 letters/digits (spaces and dashes are ignored).");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Color).NotEmpty().MaximumLength(30);
    }

    private static bool BeAValidPlate(string plate)
    {
        try { Vehicle.NormalizePlate(plate); return true; }
        catch (Exception) { return false; }
    }
}

public sealed class UpdateVehicleRequestValidator : AbstractValidator<UpdateVehicleRequest>
{
    public UpdateVehicleRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Color).NotEmpty().MaximumLength(30);
    }
}
