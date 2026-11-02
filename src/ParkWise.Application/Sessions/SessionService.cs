using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Application.Pricing;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Services;

namespace ParkWise.Application.Sessions;

public sealed class SessionService(
    IAppDbContext db,
    IPricingService pricing,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock) : ISessionService
{
    private const string SessionEntity = "ParkingSession";
    private const int MaxCheckInAttempts = 4;
    private const int MaxIdempotencyKeyLength = 100;

    private static readonly Error SessionNotFound = Error.NotFound("sessions.not_found", "Session not found.");

    // ------------------------------------------------------------------ check-in

    public async Task<Result<SessionDto>> CheckInAsync(CheckInRequest request, CancellationToken ct)
    {
        var plate = Vehicle.NormalizePlate(request.PlateNumber);

        // Two attendants can race for the same last free spot, or two gates can check in the same plate at once.
        // The database arbitrates (rowversion on the spot, unique indexes on active sessions / plates); the loser's
        // SaveChanges fails, we discard that attempt and look again, now seeing the winner's data.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CheckInOnceAsync(request, plate, ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (attempt >= MaxCheckInAttempts)
                    return Error.Conflict("sessions.contention", "The lot is busy right now. Please try again.");
            }
        }
    }

    private async Task<Result<SessionDto>> CheckInOnceAsync(CheckInRequest request, string plate, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null) return Error.NotFound("lots.not_found", "Lot not found.");
        if (!lot.IsActive) return Error.Conflict("lots.inactive", "This lot is closed.");

        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.PlateNumber == plate, ct);
        var isNewVehicle = vehicle is null;

        if (vehicle is null)
        {
            if (request.VehicleType is null)
                return Error.Validation("vehicles.type_required", "This plate is not registered. Provide the vehicle type to check it in.");
            vehicle = new Vehicle(plate, request.VehicleType.Value, "Unknown"); // walk-in: no owner
        }
        else if (await db.Sessions.AnyAsync(s => s.VehicleId == vehicle.Id && s.Status == SessionStatus.Active, ct))
        {
            return Error.Conflict("sessions.vehicle_already_parked", "This vehicle already has an active parking session.");
        }

        var now = Now();
        Spot? spot = null;

        // A vehicle that reserved a spot claims it: from 15 minutes before the start until the hold ends. The spot is
        // Available (not yet held) or Reserved (hold running). If someone else sits in it, fall back to a normal spot.
        Reservation? reservation = null;
        if (!isNewVehicle)
        {
            var earliestStart = now + Reservation.EarlyArrival;      // r.Start - EarlyArrival <= now
            var holdFloor = now - Reservation.HoldDuration;          // now < r.Start + Hold
            reservation = await (from r in db.Reservations
                                 join s in db.Spots on r.SpotId equals s.Id
                                 join l in db.Levels on s.LevelId equals l.Id
                                 where r.VehicleId == vehicle.Id && r.Status == ReservationStatus.Pending && l.LotId == lot.Id
                                       && r.StartUtc <= earliestStart && r.StartUtc > holdFloor
                                 orderby r.StartUtc
                                 select r).FirstOrDefaultAsync(ct);

            if (reservation is not null)
            {
                spot = await db.Spots.SingleAsync(s => s.Id == reservation.SpotId, ct);
                if (spot.Status is not (SpotStatus.Available or SpotStatus.Reserved))
                {
                    spot = null;
                    reservation = null;
                }
            }
        }

        if (spot is null)
        {
            // Best-fitting free spot first (e.g. an EV prefers an EV spot, then Standard); lowest level and code win ties.
            // Only Available spots qualify, so Occupied, Reserved and OutOfService spots are never assigned.
            foreach (var type in Domain.Services.SpotCompatibility.PreferredSpotTypes(vehicle, lot.AllowMotorcycleOverflow))
            {
                spot = await (from s in db.Spots
                              join l in db.Levels on s.LevelId equals l.Id
                              where l.LotId == lot.Id && s.Type == type && s.Status == SpotStatus.Available
                              orderby l.Number, s.Code
                              select s).FirstOrDefaultAsync(ct);
                if (spot is not null) break;
            }
        }

        if (spot is null)
            return Error.Conflict("sessions.no_spot_available", "No compatible spot is free in this lot.");

        spot.Occupy();
        reservation?.Fulfill();
        if (isNewVehicle) db.Vehicles.Add(vehicle);

        var session = new ParkingSession(vehicle.Id, spot.Id, CodeGenerator.Ticket(), now, CurrentUserId(), reservation?.Id);
        db.Sessions.Add(session);
        audit.Log("Session.CheckedIn", SessionEntity, session.Id, after: new
        {
            session.TicketCode, vehicle.PlateNumber, SpotCode = spot.Code, SpotType = spot.Type, LotId = lot.Id, session.CheckInAtUtc,
            ReservationId = reservation?.Id,
        });

        await db.SaveChangesAsync(ct); // vehicle + spot + session + audit: one transaction

        return (await GetAsync(session.Id, ct)).Value;
    }

    // ------------------------------------------------------------------ check-out

    public async Task<Result<CheckOutOutcome>> CheckOutAsync(Guid sessionId, string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is { Length: > MaxIdempotencyKeyLength })
            return Error.Validation("sessions.invalid_idempotency_key", $"The idempotency key can be at most {MaxIdempotencyKeyLength} characters.");

        return await WithSettlementRecoveryAsync(sessionId, () => CheckOutOnceAsync(sessionId, idempotencyKey, ct), ct);
    }

    private async Task<Result<CheckOutOutcome>> CheckOutOnceAsync(Guid sessionId, string? idempotencyKey, CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return SessionNotFound;

        if (!session.IsActive) return await ExistingReceiptAsync(sessionId, ct); // idempotent: never charge twice

        var spot = await db.Spots.SingleAsync(s => s.Id == session.SpotId, ct);
        var now = Now();

        var terms = await pricing.GetTermsAsync(spot.Type, now, ct);
        if (!terms.IsSuccess) return terms.Error!;

        var breakdown = FeeCalculator.Calculate(session.CheckInAtUtc, now, terms.Value);
        return await SettleAsync(session, spot, breakdown, lostTicket: false, idempotencyKey, now, ct);
    }

    public async Task<Result<CheckOutOutcome>> LostTicketAsync(LostTicketRequest request, CancellationToken ct)
    {
        var plate = Vehicle.NormalizePlate(request.PlateNumber);

        var sessionId = await (from s in db.Sessions
                               join v in db.Vehicles on s.VehicleId equals v.Id
                               where v.PlateNumber == plate && s.Status == SessionStatus.Active
                               select (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (sessionId is null)
            return Error.NotFound("sessions.no_active_session", "That vehicle has no active parking session.");

        return await WithSettlementRecoveryAsync(sessionId.Value, async () =>
        {
            var session = await db.Sessions.SingleAsync(s => s.Id == sessionId, ct);
            if (!session.IsActive) return await ExistingReceiptAsync(session.Id, ct);

            var spot = await db.Spots.SingleAsync(s => s.Id == session.SpotId, ct);
            var now = Now();

            var fee = await pricing.GetLostTicketFeeAsync(now, ct);
            if (!fee.IsSuccess) return fee.Error!;

            // The flat fee REPLACES the normal fee: no hours, no cap.
            var breakdown = FeeCalculator.LostTicket(session.CheckInAtUtc, now, fee.Value);
            return await SettleAsync(session, spot, breakdown, lostTicket: true, idempotencyKey: null, now, ct);
        }, ct);
    }

    /// <summary>Closes the session, takes the (mock) payment, frees the spot, issues the receipt and audits it: one save.</summary>
    private async Task<Result<CheckOutOutcome>> SettleAsync(
        ParkingSession session, Spot spot, FeeBreakdown breakdown, bool lostTicket, string? idempotencyKey, DateTime now, CancellationToken ct)
    {
        session.CheckOut(now, lostTicket);

        var payment = new Payment(session.Id, breakdown.Total, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(), now);
        var receipt = new Receipt(session.Id, CodeGenerator.ReceiptNumber(now), breakdown.Total,
            JsonSerializer.Serialize(breakdown, AppJson.Options), now);
        session.AttachSettlement(payment, receipt);
        spot.Release();

        audit.Log(lostTicket ? "Session.LostTicketCheckOut" : "Session.CheckedOut", SessionEntity, session.Id,
            before: new { Status = SessionStatus.Active },
            after: new { session.Status, Amount = breakdown.Total, ReceiptNumber = receipt.Number, session.CheckOutAtUtc, breakdown.IsLostTicket });

        await db.SaveChangesAsync(ct);

        var dto = await ReceiptRows(visibleOnly: false).SingleAsync(r => r.Receipt.SessionId == session.Id, ct);
        return new CheckOutOutcome(ToDto(dto), Replayed: false);
    }

    /// <summary>
    /// Two requests can try to close the same session at once. The unique index on Payment.SessionId (and the spot's
    /// rowversion) lets exactly one commit; the other fails with DbUpdateException. We then return the WINNER's
    /// receipt, so the caller sees a normal success and nothing was charged twice.
    /// </summary>
    private async Task<Result<CheckOutOutcome>> WithSettlementRecoveryAsync(Guid sessionId, Func<Task<Result<CheckOutOutcome>>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var existing = await ExistingReceiptAsync(sessionId, ct);
            if (existing.IsSuccess) return existing;
            throw;
        }
    }

    private async Task<Result<CheckOutOutcome>> ExistingReceiptAsync(Guid sessionId, CancellationToken ct)
    {
        var row = await ReceiptRows(visibleOnly: false).SingleOrDefaultAsync(r => r.Receipt.SessionId == sessionId, ct);
        return row is null
            ? Error.Conflict("sessions.not_active", "This session is closed but has no receipt.")
            : new CheckOutOutcome(ToDto(row), Replayed: true);
    }

    // ------------------------------------------------------------------ reads

    public async Task<Result<FeePreviewDto>> PreviewFeeAsync(Guid sessionId, CancellationToken ct)
    {
        var row = await SessionRows().SingleOrDefaultAsync(r => r.Session.Id == sessionId, ct);
        if (row is null) return SessionNotFound;
        if (row.Session.Status != SessionStatus.Active)
            return Error.Conflict("sessions.not_active", "Only an active session has a fee preview. See its receipt instead.");

        var now = Now();
        var terms = await pricing.GetTermsAsync(row.Spot.Type, now, ct);
        if (!terms.IsSuccess) return terms.Error!;

        var breakdown = FeeCalculator.Calculate(row.Session.CheckInAtUtc, now, terms.Value);
        return new FeePreviewDto(sessionId, now, breakdown.Total, breakdown);
    }

    public async Task<PagedResult<SessionDto>> ListAsync(SessionListQuery query, CancellationToken ct)
    {
        var q = SessionRows();

        if (query.Status is { } status) q = q.Where(r => r.Session.Status == status);
        if (query.LotId is { } lotId) q = q.Where(r => r.Lot.Id == lotId);
        if (!string.IsNullOrWhiteSpace(query.TicketCode))
        {
            var code = query.TicketCode.Trim().ToUpperInvariant();
            q = q.Where(r => r.Session.TicketCode == code);
        }
        if (!string.IsNullOrWhiteSpace(query.Plate))
        {
            var term = new string(query.Plate.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            q = q.Where(r => r.Vehicle.PlateNumber.Contains(term));
        }
        if (query.FromUtc is { } from) q = q.Where(r => r.Session.CheckInAtUtc >= from);
        if (query.ToUtc is { } to) q = q.Where(r => r.Session.CheckInAtUtc < to);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "checkin" => q.OrderBy(r => r.Session.CheckInAtUtc),
            "plate" => q.OrderBy(r => r.Vehicle.PlateNumber).ThenByDescending(r => r.Session.CheckInAtUtc),
            "status" => q.OrderBy(r => r.Session.Status).ThenByDescending(r => r.Session.CheckInAtUtc),
            _ => q.OrderByDescending(r => r.Session.CheckInAtUtc),
        };

        var paging = query.Paging;
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct);

        return new PagedResult<SessionDto>(rows.Select(ToDto).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<Result<SessionDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await SessionRows().SingleOrDefaultAsync(r => r.Session.Id == id, ct);
        return row is null ? SessionNotFound : ToDto(row);
    }

    public async Task<Result<ReceiptDto>> GetReceiptAsync(Guid receiptId, CancellationToken ct)
    {
        var row = await ReceiptRows(visibleOnly: true).SingleOrDefaultAsync(r => r.Receipt.Id == receiptId, ct);
        return row is null ? Error.NotFound("receipts.not_found", "Receipt not found.") : ToDto(row);
    }

    // ------------------------------------------------------------------ queries & mapping

    private sealed class SessionRow
    {
        public required ParkingSession Session { get; init; }
        public required Vehicle Vehicle { get; init; }
        public required Spot Spot { get; init; }
        public required Level Level { get; init; }
        public required ParkingLot Lot { get; init; }
        public Receipt? Receipt { get; init; }
    }

    private sealed class ReceiptRow
    {
        public required Receipt Receipt { get; init; }
        public required Payment Payment { get; init; }
        public required ParkingSession Session { get; init; }
        public required Vehicle Vehicle { get; init; }
        public required Spot Spot { get; init; }
        public required Level Level { get; init; }
        public required ParkingLot Lot { get; init; }
    }

    /// <summary>Staff see every session; anyone else only sessions of vehicles they own. The rule lives in this one place.</summary>
    private IQueryable<ParkingSession> VisibleSessions(bool visibleOnly)
    {
        var q = db.Sessions.AsNoTracking().AsQueryable();
        if (!visibleOnly || currentUser.IsStaff()) return q;

        var me = currentUser.UserId;
        return q.Where(s => db.Vehicles.Any(v => v.Id == s.VehicleId && v.OwnerUserId != null && v.OwnerUserId == me));
    }

    private IQueryable<SessionRow> SessionRows()
        => (from s in VisibleSessions(visibleOnly: true)
            join v in db.Vehicles on s.VehicleId equals v.Id
            join sp in db.Spots on s.SpotId equals sp.Id
            join lv in db.Levels on sp.LevelId equals lv.Id
            join lot in db.Lots on lv.LotId equals lot.Id
            join r in db.Receipts on s.Id equals r.SessionId into receipts
            from r in receipts.DefaultIfEmpty()
            select new SessionRow { Session = s, Vehicle = v, Spot = sp, Level = lv, Lot = lot, Receipt = r })
            .AsNoTracking();

    private IQueryable<ReceiptRow> ReceiptRows(bool visibleOnly)
        => (from r in db.Receipts
            join s in VisibleSessions(visibleOnly) on r.SessionId equals s.Id
            join p in db.Payments on s.Id equals p.SessionId
            join v in db.Vehicles on s.VehicleId equals v.Id
            join sp in db.Spots on s.SpotId equals sp.Id
            join lv in db.Levels on sp.LevelId equals lv.Id
            join lot in db.Lots on lv.LotId equals lot.Id
            select new ReceiptRow { Receipt = r, Payment = p, Session = s, Vehicle = v, Spot = sp, Level = lv, Lot = lot })
            .AsNoTracking();

    private static SessionDto ToDto(SessionRow r) => new(
        r.Session.Id, r.Session.TicketCode,
        r.Vehicle.Id, r.Vehicle.PlateNumber, r.Vehicle.Type,
        r.Lot.Id, r.Lot.Name, r.Level.Number, r.Spot.Id, r.Spot.Code, r.Spot.Type,
        r.Session.CheckInAtUtc, r.Session.CheckOutAtUtc, r.Session.Status,
        r.Receipt?.Id, r.Receipt?.Number, r.Receipt?.Amount, r.Session.ReservationId);

    private static ReceiptDto ToDto(ReceiptRow r) => new(
        r.Receipt.Id, r.Receipt.Number, r.Session.Id, r.Session.TicketCode, r.Vehicle.PlateNumber,
        r.Lot.Name, r.Level.Number, r.Spot.Code,
        r.Session.CheckInAtUtc, r.Session.CheckOutAtUtc!.Value,
        r.Receipt.Amount, r.Payment.Method, r.Receipt.IssuedAtUtc,
        JsonSerializer.Deserialize<FeeBreakdown>(r.Receipt.BreakdownJson, AppJson.Options)!);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private Guid CurrentUserId()
        => currentUser.UserId ?? throw new InvalidOperationException("Check-in requires an authenticated user.");
}
