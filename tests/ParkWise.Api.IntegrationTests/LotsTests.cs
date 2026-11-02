using System.Net;
using System.Net.Http.Json;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LotsTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Anonymous_users_cannot_read_lots()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().GetAsync("/api/v1/lots")).StatusCode);

    [Fact]
    public async Task Any_signed_in_user_can_read_the_seeded_lots()
    {
        var (driver, _) = await _arrange.NewDriverAsync();

        var page = await (await driver.GetAsync("/api/v1/lots?search=Garage")).ReadAsync<PagedResult<LotDto>>();
        var downtown = Assert.Single(page.Items, l => l.Name == "Downtown Garage");
        Assert.Equal((3, 32), (downtown.LevelCount, downtown.SpotCount));

        var detail = await (await driver.GetAsync($"/api/v1/lots/{downtown.Id}")).ReadAsync<LotDetailDto>();
        Assert.Equal([1, 2, 3], detail.Levels.Select(l => l.Number));
        Assert.Equal(32, detail.Levels.Sum(l => l.SpotCount));
    }

    [Fact]
    public async Task Only_admins_can_create_lots()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var attendant = await _arrange.AttendantAsync();
        var body = new SaveLotRequest(Arrange.Unique("Lot"), "Addr", false);

        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostJsonAsync("/api/v1/lots", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.PostJsonAsync("/api/v1/lots", body)).StatusCode);
    }

    [Fact]
    public async Task Admin_creates_a_lot_and_duplicate_names_conflict_case_insensitively()
    {
        var admin = await _arrange.AdminAsync();
        var name = Arrange.Unique("Lot");

        var created = await admin.PostJsonAsync("/api/v1/lots", new SaveLotRequest(name, "Addr", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var lot = await created.ReadAsync<LotDto>();
        Assert.Equal($"/api/v1/lots/{lot.Id}", created.Headers.Location!.ToString());
        Assert.True(lot.IsActive);
        Assert.True(lot.AllowMotorcycleOverflow);

        var duplicate = await admin.PostJsonAsync("/api/v1/lots", new SaveLotRequest(name.ToUpperInvariant(), "Other", false));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("lots.name_taken", await ProblemCodeAsync(duplicate));
    }

    [Fact]
    public async Task Invalid_lot_returns_400_with_field_errors()
    {
        var admin = await _arrange.AdminAsync();
        var response = await admin.PostJsonAsync("/api/v1/lots", new SaveLotRequest("", new string('x', 301), false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task Update_changes_the_lot_and_unknown_ids_return_404()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);

        var updated = await (await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}", new SaveLotRequest(lot.Name, "New Address", true))).ReadAsync<LotDto>();
        Assert.Equal("New Address", updated.Address);
        Assert.True(updated.AllowMotorcycleOverflow);

        var missing = await admin.PutJsonAsync($"/api/v1/lots/{Guid.NewGuid()}", new SaveLotRequest("X", "Y", false));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_lot_to_another_lots_name_conflicts()
    {
        var admin = await _arrange.AdminAsync();
        var a = await _arrange.LotAsync(admin);
        var b = await _arrange.LotAsync(admin);

        var response = await admin.PutJsonAsync($"/api/v1/lots/{b.Id}", new SaveLotRequest(a.Name, "Addr", false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task List_supports_search_paging_sorting_and_active_filter()
    {
        var admin = await _arrange.AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var names = new[] { $"Lot{tag}-B", $"Lot{tag}-A", $"Lot{tag}-C" };
        foreach (var n in names) await admin.PostJsonAsync("/api/v1/lots", new SaveLotRequest(n, "Addr", false));

        var page1 = await (await admin.GetAsync($"/api/v1/lots?search={tag}&pageSize=2&sort=name")).ReadAsync<PagedResult<LotDto>>();
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal([$"Lot{tag}-A", $"Lot{tag}-B"], page1.Items.Select(l => l.Name));

        var page2 = await (await admin.GetAsync($"/api/v1/lots?search={tag}&pageSize=2&page=2&sort=name_desc")).ReadAsync<PagedResult<LotDto>>();
        Assert.Equal([$"Lot{tag}-A"], page2.Items.Select(l => l.Name));

        var inactive = await (await admin.GetAsync($"/api/v1/lots?search={tag}&isActive=false")).ReadAsync<PagedResult<LotDto>>();
        Assert.Equal(0, inactive.TotalCount);
    }

    [Fact]
    public async Task Page_size_is_capped_at_100()
    {
        var admin = await _arrange.AdminAsync();
        var page = await (await admin.GetAsync("/api/v1/lots?pageSize=100000")).ReadAsync<PagedResult<LotDto>>();
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task Deactivate_is_idempotent_and_hides_nothing_else()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false })).StatusCode);

        var detail = await (await admin.GetAsync($"/api/v1/lots/{lot.Id}")).ReadAsync<LotDetailDto>();
        Assert.False(detail.IsActive);
    }

    [Fact]
    public async Task A_lot_with_parked_vehicles_cannot_be_deactivated()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);

        var response = await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("lots.has_active_sessions", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_lot_with_pending_reservations_cannot_be_deactivated()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.AddPendingReservationAsync(spot.Id, vehicle.Id);

        var response = await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });

        Assert.Equal("lots.has_pending_reservations", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_lot_without_history_can_be_deleted_with_its_levels_and_spots()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, spot) = await _arrange.LotWithSpotAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/lots/{lot.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/lots/{lot.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/spots/{spot.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_lot_with_history_cannot_be_deleted_even_after_the_vehicle_left()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, spot) = await _arrange.LotWithSpotAsync(admin);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);
        await _arrange.EndSessionAsync(spot.Id); // no active session any more, but history remains

        var response = await admin.DeleteAsync($"/api/v1/lots/{lot.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("lots.has_history", await ProblemCodeAsync(response));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/lots/{lot.Id}")).StatusCode);

        // ...but deactivating is allowed (decision 2: deactivate instead of delete).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false })).StatusCode);
    }

    [Fact]
    public async Task Every_admin_change_is_audited_with_actor_before_and_after()
    {
        var admin = await _arrange.AdminAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me")).ReadAsync<ParkWise.Application.Auth.UserDto>();
        var lot = await _arrange.LotAsync(admin);
        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}", new SaveLotRequest(lot.Name, "Changed", false));
        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });
        await admin.DeleteAsync($"/api/v1/lots/{lot.Id}");

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityType=ParkingLot&entityId={lot.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        // newest first
        Assert.Equal(["Lot.Deleted", "Lot.Deactivated", "Lot.Updated", "Lot.Created"], logs.Items.Select(a => a.Action));
        Assert.All(logs.Items, a => Assert.Equal(me.Id, a.ActorUserId));
        Assert.All(logs.Items, a => Assert.Equal(DateTimeKind.Utc, a.TimestampUtc.Kind));

        var updated = logs.Items.Single(a => a.Action == "Lot.Updated");
        Assert.Contains("1 Test Street", updated.BeforeJson);
        Assert.Contains("Changed", updated.AfterJson);
        Assert.Null(logs.Items.Single(a => a.Action == "Lot.Created").BeforeJson);
    }

    [Fact]
    public async Task A_failed_change_leaves_no_audit_entry()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var other = await _arrange.LotAsync(admin);

        // Rename `other` onto `lot`'s name: rejected with 409, so nothing may be audited for `other` except its creation.
        await admin.PutJsonAsync($"/api/v1/lots/{other.Id}", new SaveLotRequest(lot.Name, "Addr", false));

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={other.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Equal(["Lot.Created"], logs.Items.Select(a => a.Action));
    }

    [Fact]
    public async Task Audit_logs_are_admin_only()
    {
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.GetAsync("/api/v1/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/v1/audit-logs")).StatusCode);
    }

    // ---- levels ----
    [Fact]
    public async Task Levels_can_be_added_renamed_and_duplicates_conflict()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var level = await _arrange.LevelAsync(admin, lot.Id, number: 2);

        var duplicate = await admin.PostJsonAsync($"/api/v1/lots/{lot.Id}/levels", new CreateLevelRequest(2, "Again"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("levels.number_taken", await ProblemCodeAsync(duplicate));

        var renamed = await (await admin.PutJsonAsync($"/api/v1/levels/{level.Id}", new UpdateLevelRequest("Roof"))).ReadAsync<LevelDto>();
        Assert.Equal("Roof", renamed.Name);
        Assert.Equal(2, renamed.Number);
    }

    [Fact]
    public async Task Level_with_history_cannot_be_deleted_but_an_empty_one_can()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, level, spot) = await _arrange.LotWithSpotAsync(admin);
        var empty = await _arrange.LevelAsync(admin, lot.Id, number: 5);
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, vehicle.Id);

        var blocked = await admin.DeleteAsync($"/api/v1/levels/{level.Id}");
        Assert.Equal("levels.has_history", await ProblemCodeAsync(blocked));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/levels/{empty.Id}")).StatusCode);
    }

    [Fact]
    public async Task Adding_a_level_to_an_unknown_lot_returns_404()
    {
        var admin = await _arrange.AdminAsync();
        var response = await admin.PostJsonAsync($"/api/v1/lots/{Guid.NewGuid()}/levels", new CreateLevelRequest(1, "X"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
