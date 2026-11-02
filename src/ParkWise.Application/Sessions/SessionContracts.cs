using FluentValidation;
using ParkWise.Application.Common;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Sessions;

/// <param name="VehicleType">Only needed when the plate has never been seen before (a walk-in).</param>
public sealed record CheckInRequest(Guid LotId, string PlateNumber, VehicleType? VehicleType);
public sealed record LostTicketRequest(string PlateNumber);

public sealed record SessionDto(
    Guid Id, string TicketCode,
    Guid VehicleId, string PlateNumber, VehicleType VehicleType,
    Guid LotId, string LotName, int LevelNumber, Guid SpotId, string SpotCode, SpotType SpotType,
    DateTime CheckInAtUtc, DateTime? CheckOutAtUtc, SessionStatus Status,
    Guid? ReceiptId, string? ReceiptNumber, decimal? AmountPaid, Guid? ReservationId);

public sealed record FeePreviewDto(Guid SessionId, DateTime AtUtc, decimal Amount, FeeBreakdown Breakdown);

public sealed record ReceiptDto(
    Guid Id, string Number, Guid SessionId, string TicketCode, string PlateNumber,
    string LotName, int LevelNumber, string SpotCode,
    DateTime CheckInAtUtc, DateTime CheckOutAtUtc,
    decimal Amount, PaymentMethod PaymentMethod, DateTime IssuedAtUtc, FeeBreakdown Breakdown);

/// <param name="Replayed">True when this check-out had already been completed and the existing receipt is returned.</param>
public sealed record CheckOutOutcome(ReceiptDto Receipt, bool Replayed);

public sealed record SessionListQuery(
    SessionStatus? Status, string? Plate, string? TicketCode, Guid? LotId,
    DateTime? FromUtc, DateTime? ToUtc, string? Sort, int Page = 1, int PageSize = 20)
{
    public PageQuery Paging => new(Page, PageSize);
}

public interface ISessionService
{
    Task<Result<SessionDto>> CheckInAsync(CheckInRequest request, CancellationToken ct);
    Task<Result<CheckOutOutcome>> CheckOutAsync(Guid sessionId, string? idempotencyKey, CancellationToken ct);
    Task<Result<CheckOutOutcome>> LostTicketAsync(LostTicketRequest request, CancellationToken ct);
    Task<Result<FeePreviewDto>> PreviewFeeAsync(Guid sessionId, CancellationToken ct);
    Task<PagedResult<SessionDto>> ListAsync(SessionListQuery query, CancellationToken ct);
    Task<Result<SessionDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<ReceiptDto>> GetReceiptAsync(Guid receiptId, CancellationToken ct);
}

public sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator()
    {
        RuleFor(x => x.LotId).NotEmpty();
        RuleFor(x => x.PlateNumber).Must(PlateRules.IsValid).WithMessage("Plate number must be 2-10 letters/digits (spaces and dashes are ignored).");
        RuleFor(x => x.VehicleType!.Value).IsInEnum().When(x => x.VehicleType.HasValue).WithName("vehicleType");
    }
}

public sealed class LostTicketRequestValidator : AbstractValidator<LostTicketRequest>
{
    public LostTicketRequestValidator()
        => RuleFor(x => x.PlateNumber).Must(PlateRules.IsValid).WithMessage("Plate number must be 2-10 letters/digits (spaces and dashes are ignored).");
}
