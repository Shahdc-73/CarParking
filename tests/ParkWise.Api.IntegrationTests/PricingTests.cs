using System.Net;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Pricing;
using ParkWise.Application.Sessions;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

// NOTE: pricing is global. These tests only change MOTORCYCLE pricing (and a far-future lost ticket fee), so the
// fee tests for Standard/EV and the 50.00 lost ticket fee stay stable.
[Collection(ApiCollection.Name)]
public class PricingTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Everyone_signed_in_can_read_the_current_prices()
    {
        var (driver, _) = await _arrange.NewDriverAsync();

        var pricing = await (await driver.GetAsync("/api/v1/pricing")).ReadAsync<PricingOverviewDto>();

        Assert.Equal(5, pricing.Rules.Count);
        var standard = pricing.Rules.Single(r => r.SpotType == SpotType.Standard);
        Assert.Equal((3.00m, 20.00m, 15), (standard.HourlyRate, standard.DailyCap, standard.FreeMinutes));
        Assert.Equal(3.50m, pricing.Rules.Single(r => r.SpotType == SpotType.EV).HourlyRate);
        Assert.Equal(50.00m, pricing.LostTicketFee!.Amount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().GetAsync("/api/v1/pricing")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_change_prices()
    {
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var body = new SavePricingRuleRequest(2m, 10m, 15, null);

        foreach (var client in new[] { attendant, driver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutJsonAsync("/api/v1/pricing/Motorcycle", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutJsonAsync("/api/v1/pricing/lost-ticket-fee", new SaveLostTicketFeeRequest(60m, null))).StatusCode);
        }
    }

    [Theory]
    [InlineData(-1, 10, 15)]      // negative rate
    [InlineData(5, 4, 15)]        // cap below rate
    [InlineData(2, 10, -1)]       // negative free minutes
    [InlineData(2, 10, 241)]      // absurd free period
    [InlineData(2.005, 10, 15)]   // more than 2 decimals
    [InlineData(1001, 2000, 15)]  // absurd rate
    public async Task Invalid_prices_are_rejected_with_400(double rate, double cap, int free)
    {
        var admin = await _arrange.AdminAsync();
        var response = await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest((decimal)rate, (decimal)cap, free, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_spot_type_or_a_start_in_the_past_is_rejected()
    {
        var admin = await _arrange.AdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync("/api/v1/pricing/Hovercraft", new SavePricingRuleRequest(2m, 10m, 15, null))).StatusCode);

        var past = await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(2m, 10m, 15, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal("pricing.effective_in_past", await ProblemCodeAsync(past));
    }

    [Fact]
    public async Task A_price_change_adds_a_new_version_takes_effect_now_and_is_audited()
    {
        var admin = await _arrange.AdminAsync();

        var changed = await (await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(1.75m, 12m, 10, null))).ReadAsync<PricingRuleDto>();

        Assert.Equal((SpotType.Motorcycle, 1.75m, 12m, 10), (changed.SpotType, changed.HourlyRate, changed.DailyCap, changed.FreeMinutes));
        var current = (await (await admin.GetAsync("/api/v1/pricing")).ReadAsync<PricingOverviewDto>()).Rules.Single(r => r.SpotType == SpotType.Motorcycle);
        Assert.Equal(1.75m, current.HourlyRate);

        var logs = await (await admin.GetAsync("/api/v1/audit-logs?action=Pricing.RuleChanged")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Contains("1.75", logs.Items[0].AfterJson);
        Assert.NotNull(logs.Items[0].BeforeJson); // what it replaced
    }

    [Fact]
    public async Task A_price_scheduled_for_the_future_does_not_apply_yet()
    {
        var admin = await _arrange.AdminAsync();
        await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(1.50m, 10m, 15, null));

        var scheduled = await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(9m, 50m, 15, DateTime.UtcNow.AddDays(400)));

        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var current = (await (await admin.GetAsync("/api/v1/pricing")).ReadAsync<PricingOverviewDto>()).Rules.Single(r => r.SpotType == SpotType.Motorcycle);
        Assert.Equal(1.50m, current.HourlyRate);

        // The same instant cannot be scheduled twice.
        var dto = await scheduled.ReadAsync<PricingRuleDto>();
        var duplicate = await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(8m, 40m, 15, dto.EffectiveFromUtc));
        Assert.Equal("pricing.already_scheduled", await ProblemCodeAsync(duplicate));
    }

    [Fact]
    public async Task The_lost_ticket_fee_can_be_scheduled_without_changing_todays_fee()
    {
        var admin = await _arrange.AdminAsync();

        var response = await admin.PutJsonAsync("/api/v1/pricing/lost-ticket-fee", new SaveLostTicketFeeRequest(75m, DateTime.UtcNow.AddDays(500)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50.00m, (await (await admin.GetAsync("/api/v1/pricing")).ReadAsync<PricingOverviewDto>()).LostTicketFee!.Amount);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutJsonAsync("/api/v1/pricing/lost-ticket-fee", new SaveLostTicketFeeRequest(-5m, null))).StatusCode);
    }

    [Fact]
    public async Task Changing_prices_never_alters_receipts_that_were_already_issued()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(1.50m, 10m, 15, null)); // known baseline
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Motorcycle, SpotType.Motorcycle);
        var before = await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.Motorcycle);
        var after = await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.Motorcycle);

        ReceiptDto oldReceipt;
        using (factory.Clock.TravelBy(TimeSpan.FromHours(2)))
            oldReceipt = await (await Arrange.CheckOutAsync(attendant, before.Id)).ReadAsync<ReceiptDto>();
        Assert.Equal(3.00m, oldReceipt.Amount); // 2 hours at 1.50

        await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(4m, 30m, 15, null));

        // The old receipt is frozen, with the terms it was calculated under.
        var reread = await (await attendant.GetAsync($"/api/v1/receipts/{oldReceipt.Id}")).ReadAsync<ReceiptDto>();
        Assert.Equal((3.00m, 1.50m, 10m), (reread.Amount, reread.Breakdown.HourlyRate, reread.Breakdown.DailyCap));

        // A stay that ends after the change uses the new price.
        ReceiptDto newReceipt;
        using (factory.Clock.TravelBy(TimeSpan.FromHours(2)))
            newReceipt = await (await Arrange.CheckOutAsync(attendant, after.Id)).ReadAsync<ReceiptDto>();
        Assert.Equal(8.00m, newReceipt.Amount);
        Assert.Equal(4m, newReceipt.Breakdown.HourlyRate);
    }

    [Fact]
    public async Task Free_minutes_are_configurable_per_spot_type()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        await admin.PutJsonAsync("/api/v1/pricing/Motorcycle", new SavePricingRuleRequest(2m, 10m, 60, null));
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Motorcycle);
        var session = await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.Motorcycle);

        ReceiptDto receipt;
        using (factory.Clock.TravelBy(TimeSpan.FromMinutes(50)))
            receipt = await (await Arrange.CheckOutAsync(attendant, session.Id)).ReadAsync<ReceiptDto>();

        Assert.Equal(0.00m, receipt.Amount);     // inside the 60 free minutes
        Assert.Equal(60, receipt.Breakdown.FreeMinutes);
    }
}
