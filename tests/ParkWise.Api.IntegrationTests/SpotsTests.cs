using System.Net;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SpotsTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Only_admins_can_modify_spots_but_staff_and_drivers_can_read()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, level, spot) = await _arrange.LotWithSpotAsync(admin);
        var (driver, _) = await _arrange.NewDriverAsync();
        var attendant = await _arrange.AttendantAsync();

        foreach (var client in new[] { driver, attendant })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync($"/api/v1/levels/{level.Id}/spots", new CreateSpotRequest("B-01", SpotType.EV))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutJsonAsync($"/api/v1/spots/{spot.Id}/service", new { inService = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/v1/spots/{spot.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/spots/{spot.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/lots/{lot.Id}/spots")).StatusCode);
        }
    }

    [Fact]
    public async Task Create_normalizes_the_code_and_rejects_duplicates_and_bad_codes()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var level = await _arrange.LevelAsync(admin, lot.Id);

        var spot = await _arrange.SpotAsync(admin, level.Id, "ab-01", SpotType.Compact);
        Assert.Equal("AB-01", spot.Code);
        Assert.Equal(SpotStatus.Available, spot.Status);
        Assert.Equal(lot.Id, spot.LotId);

        var duplicate = await admin.PostJsonAsync($"/api/v1/levels/{level.Id}/spots", new CreateSpotRequest("AB-01", SpotType.Standard));
        Assert.Equal("spots.code_taken", await ProblemCodeAsync(duplicate));

        var bad = await admin.PostJsonAsync($"/api/v1/levels/{level.Id}/spots", new CreateSpotRequest("no spaces!", SpotType.Standard));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // The same code on a DIFFERENT level is fine (uniqueness is per level).
        var level2 = await _arrange.LevelAsync(admin, lot.Id, number: 2);
        await _arrange.SpotAsync(admin, level2.Id, "AB-01");
    }

    [Fact]
    public async Task Enums_are_sent_and_received_as_strings()
    {
        var admin = await _arrange.AdminAsync();
        var (_, level, _) = await _arrange.LotWithSpotAsync(admin);

        var response = await admin.PostAsync($"/api/v1/levels/{level.Id}/spots",
            new StringContent("""{"code":"EV-1","type":"EV"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"type\":\"EV\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_invalid_enum_value_is_rejected_with_400()
    {
        var admin = await _arrange.AdminAsync();
        var (_, level, _) = await _arrange.LotWithSpotAsync(admin);

        var response = await admin.PostAsync($"/api/v1/levels/{level.Id}/spots",
            new StringContent("""{"code":"X-1","type":"Hovercraft"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_filters_by_level_type_status_and_search_with_paging_and_sorting()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var l1 = await _arrange.LevelAsync(admin, lot.Id, 1);
        var l2 = await _arrange.LevelAsync(admin, lot.Id, 2);
        foreach (var code in new[] { "S-03", "S-01", "S-02" }) await _arrange.SpotAsync(admin, l1.Id, code);
        var ev = await _arrange.SpotAsync(admin, l1.Id, "E-01", SpotType.EV);
        var other = await _arrange.SpotAsync(admin, l2.Id, "S-01");
        await admin.PutJsonAsync($"/api/v1/spots/{ev.Id}/service", new { inService = false });

        async Task<PagedResult<SpotDto>> Query(string qs) => await (await admin.GetAsync($"/api/v1/lots/{lot.Id}/spots{qs}")).ReadAsync<PagedResult<SpotDto>>();

        Assert.Equal(5, (await Query("")).TotalCount);
        Assert.Equal(4, (await Query($"?levelId={l1.Id}")).TotalCount);
        Assert.Equal([ev.Id], (await Query("?type=EV")).Items.Select(s => s.Id));
        Assert.Equal([ev.Id], (await Query("?status=OutOfService")).Items.Select(s => s.Id));
        Assert.Equal(4, (await Query("?search=S-0")).TotalCount);

        // default order: level, then code
        Assert.Equal(["E-01", "S-01", "S-02", "S-03", "S-01"], (await Query("")).Items.Select(s => s.Code));
        Assert.Equal(other.Id, (await Query("")).Items.Last().Id);

        var page = await Query("?pageSize=2&page=2&sort=code_desc");
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"code":123,"type":"Standard"}""")]
    public async Task Unreadable_request_bodies_return_400_never_500(string body)
    {
        var admin = await _arrange.AdminAsync();
        var (_, level, _) = await _arrange.LotWithSpotAsync(admin);

        var response = await admin.PostAsync($"/api/v1/levels/{level.Id}/spots", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Listing_spots_of_an_unknown_lot_returns_404()
    {
        var admin = await _arrange.AdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/lots/{Guid.NewGuid()}/spots")).StatusCode);
    }

    [Fact]
    public async Task Update_can_rename_and_retype_a_free_spot_and_rejects_a_taken_code()
    {
        var admin = await _arrange.AdminAsync();
        var (_, level, spot) = await _arrange.LotWithSpotAsync(admin);
        await _arrange.SpotAsync(admin, level.Id, "A-02");

        var updated = await (await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("a-09", SpotType.EV))).ReadAsync<SpotDto>();
        Assert.Equal(("A-09", SpotType.EV), (updated.Code, updated.Type));

        var clash = await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("A-02", SpotType.EV));
        Assert.Equal("spots.code_taken", await ProblemCodeAsync(clash));
    }

    [Fact]
    public async Task Decision3_changing_the_type_of_an_occupied_spot_is_blocked()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);

        var response = await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("A-01", SpotType.EV));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("domain.rule_violated", await ProblemCodeAsync(response));
        var unchanged = await (await admin.GetAsync($"/api/v1/spots/{spot.Id}")).ReadAsync<SpotDto>();
        Assert.Equal(SpotType.Standard, unchanged.Type);
    }

    [Fact]
    public async Task Decision3_changing_the_type_of_a_spot_with_a_pending_reservation_is_blocked()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.AddPendingReservationAsync(spot.Id, vehicle.Id);

        var response = await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("A-01", SpotType.EV));

        Assert.Equal("spots.has_pending_reservations", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_spot_can_go_out_of_service_and_come_back_idempotently()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);

        for (var i = 0; i < 2; i++)
        {
            var off = await (await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}/service", new { inService = false })).ReadAsync<SpotDto>();
            Assert.Equal(SpotStatus.OutOfService, off.Status);
        }
        for (var i = 0; i < 2; i++)
        {
            var on = await (await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}/service", new { inService = true })).ReadAsync<SpotDto>();
            Assert.Equal(SpotStatus.Available, on.Status);
        }
    }

    [Fact]
    public async Task An_occupied_spot_cannot_be_taken_out_of_service()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);

        var response = await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}/service", new { inService = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Decision2_a_spot_with_history_cannot_be_deleted_but_an_unused_one_can()
    {
        var admin = await _arrange.AdminAsync();
        var (_, level, used) = await _arrange.LotWithSpotAsync(admin);
        var unused = await _arrange.SpotAsync(admin, level.Id, "A-99");
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(used.Id, vehicle.Id);
        await _arrange.EndSessionAsync(used.Id);

        var blocked = await admin.DeleteAsync($"/api/v1/spots/{used.Id}");
        Assert.Equal("spots.has_history", await ProblemCodeAsync(blocked));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/spots/{unused.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/spots/{unused.Id}")).StatusCode);
    }

    [Fact]
    public async Task Spot_changes_are_audited_in_the_same_transaction()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("A-01", SpotType.Compact));
        await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}/service", new { inService = false });

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={spot.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        Assert.Equal(["Spot.TakenOutOfService", "Spot.Updated", "Spot.Created"], logs.Items.Select(a => a.Action));
        Assert.Contains("OutOfService", logs.Items[0].AfterJson);
        Assert.Contains("Standard", logs.Items[1].BeforeJson);
        Assert.Contains("Compact", logs.Items[1].AfterJson);
    }

    [Fact]
    public async Task A_blocked_change_writes_no_audit_entry()
    {
        var admin = await _arrange.AdminAsync();
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);

        await admin.PutJsonAsync($"/api/v1/spots/{spot.Id}", new UpdateSpotRequest("A-01", SpotType.EV)); // 409

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={spot.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Equal(["Spot.Created"], logs.Items.Select(a => a.Action));
    }

    // ---- availability ----
    [Fact]
    public async Task Availability_counts_every_status_by_type_and_level()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var l1 = await _arrange.LevelAsync(admin, lot.Id, 1);
        var empty = await _arrange.LevelAsync(admin, lot.Id, 2);
        var s1 = await _arrange.SpotAsync(admin, l1.Id, "S-1");
        await _arrange.SpotAsync(admin, l1.Id, "S-2");
        var ev = await _arrange.SpotAsync(admin, l1.Id, "E-1", SpotType.EV);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(s1.Id, vehicle.Id);
        await admin.PutJsonAsync($"/api/v1/spots/{ev.Id}/service", new { inService = false });

        var a = await (await admin.GetAsync($"/api/v1/lots/{lot.Id}/availability")).ReadAsync<LotAvailabilityDto>();

        Assert.Equal(new AvailabilityCounts(3, 1, 1, 0, 1), a.Totals);
        Assert.Equal(new AvailabilityCounts(2, 1, 1, 0, 0), a.ByType.Single(t => t.Type == SpotType.Standard).Counts);
        Assert.Equal(new AvailabilityCounts(1, 0, 0, 0, 1), a.ByType.Single(t => t.Type == SpotType.EV).Counts);
        Assert.Equal(2, a.ByLevel.Count); // the empty level is listed too
        Assert.Equal(new AvailabilityCounts(0, 0, 0, 0, 0), a.ByLevel.Single(l => l.LevelId == empty.Id).Counts);
        Assert.Equal(3, a.ByLevel.Single(l => l.LevelId == l1.Id).Counts.Total);
    }

    [Fact]
    public async Task Availability_of_the_seeded_downtown_garage_matches_the_seed()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var lots = await (await driver.GetAsync("/api/v1/lots?search=Downtown")).ReadAsync<PagedResult<LotDto>>();

        var a = await (await driver.GetAsync($"/api/v1/lots/{lots.Items.Single().Id}/availability")).ReadAsync<LotAvailabilityDto>();

        Assert.Equal(32, a.Totals.Total);
        Assert.Equal(32, a.Totals.Available);
        int Of(SpotType t) => a.ByType.Single(x => x.Type == t).Counts.Total;
        Assert.Equal((21, 4, 3, 2, 2), (Of(SpotType.Standard), Of(SpotType.Compact), Of(SpotType.EV), Of(SpotType.Disabled), Of(SpotType.Motorcycle)));
    }

    [Fact]
    public async Task Availability_of_an_unknown_lot_returns_404()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await driver.GetAsync($"/api/v1/lots/{Guid.NewGuid()}/availability")).StatusCode);
    }
}
