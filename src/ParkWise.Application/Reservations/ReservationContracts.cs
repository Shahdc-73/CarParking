using FluentValidation;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Application.Reservations;

public sealed record CreateReservationRequest(Guid LotId, Guid VehicleId, SpotType SpotType, DateTime StartUtc, DateTime EndUtc);

public sealed record ReservationDto(
    Guid Id, Guid VehicleId, string PlateNumber,
    Guid LotId, string LotName, int LevelNumber, Guid SpotId, string SpotCode, SpotType SpotType,
    DateTime StartUtc, DateTime EndUtc, DateTime HoldExpiresAtUtc, ReservationStatus Status, Guid BookedByUserId);

public sealed record ReservationListQuery(
    ReservationStatus? Status, Guid? LotId, Guid? VehicleId, DateTime? FromUtc, DateTime? ToUtc, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

/// <summary>"How many spots of this type could still be reserved for this window?"</summary>
public sealed record ReservationAvailabilityQuery(Guid LotId, SpotType SpotType, DateTime StartUtc, DateTime EndUtc);
public sealed record ReservationAvailabilityDto(Guid LotId, SpotType SpotType, DateTime StartUtc, DateTime EndUtc, int AvailableSpots);

public interface IReservationService
{
    Task<Result<ReservationDto>> CreateAsync(CreateReservationRequest request, CancellationToken ct);
    Task<PagedResult<ReservationDto>> ListAsync(ReservationListQuery query, CancellationToken ct);
    Task<Result<ReservationDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result> CancelAsync(Guid id, CancellationToken ct);
    Task<Result<ReservationAvailabilityDto>> GetAvailabilityAsync(ReservationAvailabilityQuery query, CancellationToken ct);
}

/// <summary>What one run of the background job did.</summary>
public sealed record MaintenanceResult(int Expired, int HoldsStarted, int Reassigned, int CancelledNoSpot);

/// <summary>The work of the background job, as a plain service so tests can run it on demand instead of waiting for a timer.</summary>
public interface IReservationMaintenance
{
    Task<MaintenanceResult> RunOnceAsync(CancellationToken ct);
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.LotId).NotEmpty();
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.SpotType).IsInEnum();
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc).WithMessage("The end must be after the start.");
        RuleFor(x => x).Must(r => r.EndUtc - r.StartUtc >= Reservation.MinDuration)
            .When(r => r.EndUtc > r.StartUtc).WithName("endUtc")
            .WithMessage($"A reservation must last at least {Reservation.MinDuration.TotalMinutes:0} minutes.");
        RuleFor(x => x).Must(r => r.EndUtc - r.StartUtc <= Reservation.MaxDuration)
            .When(r => r.EndUtc > r.StartUtc).WithName("endUtc")
            .WithMessage($"A reservation can last at most {Reservation.MaxDuration.TotalHours:0} hours.");
    }
}

public sealed class ReservationAvailabilityQueryValidator : AbstractValidator<ReservationAvailabilityQuery>
{
    public ReservationAvailabilityQueryValidator()
    {
        RuleFor(x => x.LotId).NotEmpty();
        RuleFor(x => x.SpotType).IsInEnum();
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc).WithMessage("The end must be after the start.");
    }
}
