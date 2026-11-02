using System.Net;
using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Pricing;
using ParkWise.Application.Sessions;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SessionCheckOutTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    // Seeded Standard pricing: 3.00/hour, 20.00 daily cap, 15 free minutes.
    private async Task<(HttpClient Admin, HttpClient Attendant, SessionDto Session, Guid SpotId)> ParkedAsync(SpotType spot = SpotType.Standard, VehicleType vehicle = VehicleType.Car)
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, spot);
        var session = await _arrange.CheckedInAsync(attendant, lot.Id, type: vehicle);
        return (admin, attendant, session, spots[0].Id);
    }

    private async Task<ReceiptDto> CheckOutAfterAsync(HttpClient attendant, Guid sessionId, TimeSpan stay)
    {
        using var _ = factory.Clock.TravelBy(stay);
        var response = await Arrange.CheckOutAsync(attendant, sessionId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ReceiptDto>();
    }

    [Fact]
    public async Task Check_out_charges_the_fee_frees_the_spot_closes_the_session_and_issues_a_receipt()
    {
        var (_, attendant, session, spotId) = await ParkedAsync();

        var receipt = await CheckOutAfterAsync(attendant, session.Id, TimeSpan.FromHours(2)); // 1h45 billable -> 2 started hours

        Assert.Equal(6.00m, receipt.Amount);
        Assert.Matches("^RC-\\d{8}-[A-Z2-9]{6}$", receipt.Number);
        Assert.Equal((session.Id, session.TicketCode, session.PlateNumber), (receipt.SessionId, receipt.TicketCode, receipt.PlateNumber));
        Assert.Equal(PaymentMethod.Mock, receipt.PaymentMethod);
        Assert.Equal((2, 0, false), (receipt.Breakdown.BilledHours, receipt.Breakdown.DaysCapped, receipt.Breakdown.IsLostTicket));
        Assert.Equal((3.00m, 20.00m, 15), (receipt.Breakdown.HourlyRate, receipt.Breakdown.DailyCap, receipt.Breakdown.FreeMinutes));
        Assert.InRange(receipt.CheckOutAtUtc - receipt.CheckInAtUtc, TimeSpan.FromHours(2), TimeSpan.FromHours(2.1));

        var spot = await (await attendant.GetAsync($"/api/v1/spots/{spotId}")).ReadAsync<ParkWise.Application.Parking.SpotDto>();
        Assert.Equal(SpotStatus.Available, spot.Status);

        var closed = await (await attendant.GetAsync($"/api/v1/sessions/{session.Id}")).ReadAsync<SessionDto>();
        Assert.Equal((SessionStatus.Completed, 6.00m, receipt.Id), (closed.Status, closed.AmountPaid, closed.ReceiptId));
        Assert.NotNull(closed.CheckOutAtUtc);
    }

    [Fact]
    public async Task Leaving_within_the_free_period_costs_nothing_but_still_gets_a_receipt()
    {
        var (_, attendant, session, _) = await ParkedAsync();

        var response = await Arrange.CheckOutAsync(attendant, session.Id); // seconds after check-in

        var receipt = await response.ReadAsync<ReceiptDto>();
        Assert.Equal(0.00m, receipt.Amount);
        Assert.Equal(0, receipt.Breakdown.BilledHours);
    }

    [Theory]
    [InlineData(10, 20.00, 1)]    // 9h45 -> 10h * 3 = 30 -> capped at 20
    [InlineData(49.5, 46.00, 3)]  // 20 + 20 + (1.5h -> 2h = 6)
    public async Task The_daily_cap_applies_per_24_hour_block(double hours, double expected, int daysStarted)
    {
        var (_, attendant, session, _) = await ParkedAsync();

        var receipt = await CheckOutAfterAsync(attendant, session.Id, TimeSpan.FromHours(hours));

        Assert.Equal((decimal)expected, receipt.Amount);
        Assert.Equal(daysStarted, receipt.Breakdown.DaysStarted);
        Assert.True(receipt.Breakdown.DaysCapped >= 1);
    }

    [Fact]
    public async Task Each_spot_type_is_charged_at_its_own_rate()
    {
        var (_, attendant, session, _) = await ParkedAsync(SpotType.EV, VehicleType.ElectricCar);

        var receipt = await CheckOutAfterAsync(attendant, session.Id, TimeSpan.FromHours(2)); // EV: 3.50/h seeded

        Assert.Equal(7.00m, receipt.Amount);
    }

    [Fact]
    public async Task Checking_out_twice_returns_the_same_receipt_and_never_charges_twice()
    {
        var (_, attendant, session, _) = await ParkedAsync();
        var first = await Arrange.CheckOutAsync(attendant, session.Id);
        var firstReceipt = await first.ReadAsync<ReceiptDto>();

        // Even much later, when a fresh calculation would give a different amount.
        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(30));
        var second = await Arrange.CheckOutAsync(attendant, session.Id);
        var secondReceipt = await second.ReadAsync<ReceiptDto>();

        Assert.False(Arrange.Replayed(first));
        Assert.True(Arrange.Replayed(second));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((firstReceipt.Id, firstReceipt.Number, firstReceipt.Amount), (secondReceipt.Id, secondReceipt.Number, secondReceipt.Amount));

        await _arrange.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Payments.CountAsync(p => p.SessionId == session.Id));
            Assert.Equal(1, await db.Receipts.CountAsync(r => r.SessionId == session.Id));
        });
    }

    [Fact]
    public async Task Simultaneous_check_outs_of_one_session_produce_a_single_payment_and_every_caller_gets_the_same_receipt()
    {
        var (_, _, session, spotId) = await ParkedAsync();
        var gates = new List<HttpClient>();
        for (var i = 0; i < 8; i++) gates.Add(await _arrange.AttendantAsync());

        var responses = await Task.WhenAll(gates.Select(g => Arrange.CheckOutAsync(g, session.Id)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var receipts = await Task.WhenAll(responses.Select(r => r.ReadAsync<ReceiptDto>()));
        Assert.Single(receipts.Select(r => r.Id).Distinct());
        Assert.Equal(1, responses.Count(r => !Arrange.Replayed(r))); // exactly one caller actually performed the check-out

        await _arrange.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Payments.CountAsync(p => p.SessionId == session.Id));
            Assert.Equal(1, await db.Receipts.CountAsync(r => r.SessionId == session.Id));
            Assert.Equal(SpotStatus.Available, (await db.Spots.FindAsync(spotId))!.Status);
        });
    }

    [Fact]
    public async Task The_idempotency_key_is_stored_with_the_payment_and_cannot_be_reused_for_another_session()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var a = await _arrange.CheckedInAsync(attendant, lot.Id);
        var b = await _arrange.CheckedInAsync(attendant, lot.Id);
        var key = $"key-{Guid.NewGuid():N}";

        var okFirst = await Arrange.CheckOutAsync(attendant, a.Id, key);
        var okAgain = await Arrange.CheckOutAsync(attendant, a.Id, key);   // same key, same session: a normal retry
        var reused = await Arrange.CheckOutAsync(attendant, b.Id, key);    // same key, different session: refused

        Assert.Equal(HttpStatusCode.OK, okFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, okAgain.StatusCode);
        Assert.True(Arrange.Replayed(okAgain));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);

        await _arrange.WithDbAsync(async db =>
        {
            Assert.Equal(key, (await db.Payments.SingleAsync(p => p.SessionId == a.Id)).IdempotencyKey);
            Assert.False(await db.Payments.AnyAsync(p => p.SessionId == b.Id));
            Assert.Equal(SessionStatus.Active, (await db.Sessions.FindAsync(b.Id))!.Status); // the refused one is untouched
        });

        var tooLong = await Arrange.CheckOutAsync(attendant, b.Id, new string('k', 101));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Only_staff_can_check_out_and_unknown_sessions_return_404()
    {
        var (_, attendant, session, _) = await ParkedAsync();
        var (driver, _) = await _arrange.NewDriverAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Arrange.CheckOutAsync(driver, session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Arrange.CheckOutAsync(factory.CreateRawClient(), session.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Arrange.CheckOutAsync(attendant, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Fee_preview_shows_the_current_fee_without_ending_the_session()
    {
        var (_, attendant, session, _) = await ParkedAsync();

        FeePreviewDto preview;
        using (factory.Clock.TravelBy(TimeSpan.FromHours(2)))
            preview = await (await attendant.GetAsync($"/api/v1/sessions/{session.Id}/fee-preview")).ReadAsync<FeePreviewDto>();

        Assert.Equal(6.00m, preview.Amount);
        Assert.Equal(2, preview.Breakdown.BilledHours);
        var still = await (await attendant.GetAsync($"/api/v1/sessions/{session.Id}")).ReadAsync<SessionDto>();
        Assert.Equal(SessionStatus.Active, still.Status);

        await Arrange.CheckOutAsync(attendant, session.Id);
        var closed = await attendant.GetAsync($"/api/v1/sessions/{session.Id}/fee-preview");
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("sessions.not_active", await ProblemCodeAsync(closed));
    }

    [Fact]
    public async Task Check_out_is_audited_in_the_same_transaction()
    {
        var (admin, attendant, session, _) = await ParkedAsync();
        await Arrange.CheckOutAsync(attendant, session.Id);
        await Arrange.CheckOutAsync(attendant, session.Id); // the replay must not add a second entry

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={session.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        Assert.Equal(["Session.CheckedOut", "Session.CheckedIn"], logs.Items.Select(a => a.Action));
        Assert.Contains("Completed", logs.Items[0].AfterJson);
    }

    // ---- lost ticket ----

    [Fact]
    public async Task A_lost_ticket_charges_the_flat_fee_instead_of_the_normal_fee()
    {
        var (admin, attendant, session, spotId) = await ParkedAsync();

        var response = await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", new LostTicketRequest(session.PlateNumber.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.ReadAsync<ReceiptDto>();
        Assert.Equal(50.00m, receipt.Amount);          // seeded flat fee; a seconds-long stay would otherwise be free
        Assert.True(receipt.Breakdown.IsLostTicket);
        Assert.Equal(0, receipt.Breakdown.BilledHours);

        var closed = await (await attendant.GetAsync($"/api/v1/sessions/{session.Id}")).ReadAsync<SessionDto>();
        Assert.Equal(SessionStatus.LostTicket, closed.Status);
        var spot = await (await attendant.GetAsync($"/api/v1/spots/{spotId}")).ReadAsync<ParkWise.Application.Parking.SpotDto>();
        Assert.Equal(SpotStatus.Available, spot.Status);

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={session.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Equal("Session.LostTicketCheckOut", logs.Items[0].Action);
    }

    [Fact]
    public async Task A_lost_ticket_is_not_cheaper_or_dearer_with_a_long_stay()
    {
        var (_, attendant, session, _) = await ParkedAsync();

        HttpResponseMessage response;
        using (factory.Clock.TravelBy(TimeSpan.FromDays(5)))
            response = await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", new LostTicketRequest(session.PlateNumber));

        Assert.Equal(50.00m, (await response.ReadAsync<ReceiptDto>()).Amount);
    }

    [Fact]
    public async Task A_lost_ticket_for_a_vehicle_that_is_not_parked_returns_404_and_cannot_be_charged_twice()
    {
        var (_, attendant, session, _) = await ParkedAsync();
        var body = new LostTicketRequest(session.PlateNumber);

        Assert.Equal(HttpStatusCode.OK, (await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", body)).StatusCode);
        var again = await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", body);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("sessions.no_active_session", await ProblemCodeAsync(again));
        await _arrange.WithDbAsync(async db => Assert.Equal(1, await db.Payments.CountAsync(p => p.SessionId == session.Id)));
    }

    [Fact]
    public async Task Lost_ticket_is_staff_only_and_validates_the_plate()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var attendant = await _arrange.AttendantAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostJsonAsync("/api/v1/sessions/lost-ticket", new LostTicketRequest("AB123"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", new LostTicketRequest("!"))).StatusCode);
    }
}
