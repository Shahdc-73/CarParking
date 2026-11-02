using System.Net;
using ParkWise.Application.Common;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class VehiclesTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Anonymous_users_cannot_use_vehicles()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().GetAsync("/api/v1/vehicles")).StatusCode);

    [Fact]
    public async Task A_driver_registers_a_vehicle_they_own_with_a_normalized_plate()
    {
        var (driver, userId) = await _arrange.NewDriverAsync();
        var raw = Arrange.RandomPlate();
        var spaced = $"{raw[..3]}-{raw[3..]} ".ToLowerInvariant();

        var response = await driver.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(spaced, VehicleType.SmallCar, "Green"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var vehicle = await response.ReadAsync<VehicleDto>();
        Assert.Equal(raw, vehicle.PlateNumber);
        Assert.Equal(userId, vehicle.OwnerUserId);
        Assert.Equal($"/api/v1/vehicles/{vehicle.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Plate_numbers_are_unique_across_all_users_after_normalization()
    {
        var (driverA, _) = await _arrange.NewDriverAsync();
        var (driverB, _) = await _arrange.NewDriverAsync();
        var plate = Arrange.RandomPlate();
        await _arrange.VehicleAsync(driverA, plate);

        var response = await driverB.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest($"{plate[..4]} {plate[4..]}".ToLowerInvariant(), VehicleType.Car, "Red"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("vehicles.plate_taken", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Concurrent_registrations_of_the_same_plate_produce_exactly_one_vehicle()
    {
        var plate = Arrange.RandomPlate();
        var clients = new List<HttpClient>();
        for (var i = 0; i < 6; i++) clients.Add((await _arrange.NewDriverAsync()).Client);

        // Both the app-level check and the unique index are racing; the index is what makes this safe.
        var responses = await Task.WhenAll(clients.Select(c =>
            c.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(plate, VehicleType.Car, "Red"))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("WAYTOOLONGPLATE")]
    [InlineData("!!--!!")]
    public async Task Invalid_plates_return_400(string plate)
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var response = await driver.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(plate, VehicleType.Car, "Red"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Drivers_only_see_their_own_vehicles_and_get_404_for_others()
    {
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        await _arrange.VehicleAsync(bob);

        var aliceList = await (await alice.GetAsync("/api/v1/vehicles")).ReadAsync<PagedResult<VehicleDto>>();
        Assert.Equal([aliceCar.Id], aliceList.Items.Select(v => v.Id));

        // not 403: Bob must not learn that this vehicle exists
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/vehicles/{aliceCar.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutJsonAsync($"/api/v1/vehicles/{aliceCar.Id}", new UpdateVehicleRequest(VehicleType.Car, "Pink"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/v1/vehicles/{aliceCar.Id}")).StatusCode);

        // and nothing changed
        var still = await (await alice.GetAsync($"/api/v1/vehicles/{aliceCar.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal("Red", still.Color);
    }

    [Fact]
    public async Task Staff_see_every_vehicle_and_can_filter_and_page()
    {
        var attendant = await _arrange.AttendantAsync();
        var (a, _) = await _arrange.NewDriverAsync();
        var (b, _) = await _arrange.NewDriverAsync();
        var tag = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        await _arrange.VehicleAsync(a, $"{tag}AAA", VehicleType.Car);
        await _arrange.VehicleAsync(b, $"{tag}BBB", VehicleType.Motorcycle);
        await _arrange.VehicleAsync(b, $"{tag}CCC", VehicleType.Car);

        var all = await (await attendant.GetAsync($"/api/v1/vehicles?search={tag}&sort=plate")).ReadAsync<PagedResult<VehicleDto>>();
        Assert.Equal([$"{tag}AAA", $"{tag}BBB", $"{tag}CCC"], all.Items.Select(v => v.PlateNumber));

        var motorcycles = await (await attendant.GetAsync($"/api/v1/vehicles?search={tag}&type=Motorcycle")).ReadAsync<PagedResult<VehicleDto>>();
        Assert.Equal([$"{tag}BBB"], motorcycles.Items.Select(v => v.PlateNumber));

        var paged = await (await attendant.GetAsync($"/api/v1/vehicles?search={tag}&pageSize=2&page=2&sort=plate")).ReadAsync<PagedResult<VehicleDto>>();
        Assert.Equal((3, 1), (paged.TotalCount, paged.Items.Count));
    }

    [Fact]
    public async Task Plate_lookup_is_for_staff_only_and_matches_regardless_of_formatting()
    {
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var plate = Arrange.RandomPlate();
        var vehicle = await _arrange.VehicleAsync(driver, plate);

        var found = await (await attendant.GetAsync($"/api/v1/vehicles/lookup?plate={plate[..4].ToLowerInvariant()}-{plate[4..]}")).ReadAsync<VehicleDto>();
        Assert.Equal(vehicle.Id, found.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await attendant.GetAsync("/api/v1/vehicles/lookup?plate=NOSUCH1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await attendant.GetAsync("/api/v1/vehicles/lookup?plate=!")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync($"/api/v1/vehicles/lookup?plate={plate}")).StatusCode);
    }

    [Fact]
    public async Task Staff_registered_vehicles_are_walk_ins_without_an_owner()
    {
        var attendant = await _arrange.AttendantAsync();
        var vehicle = await _arrange.VehicleAsync(attendant);
        Assert.Null(vehicle.OwnerUserId);
    }

    [Fact]
    public async Task Only_staff_can_grant_the_disabled_permit()
    {
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();

        var byDriver = await (await driver.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(Arrange.RandomPlate(), VehicleType.Car, "Red", HasDisabledPermit: true))).ReadAsync<VehicleDto>();
        Assert.False(byDriver.HasDisabledPermit);

        var stillFalse = await (await driver.PutJsonAsync($"/api/v1/vehicles/{byDriver.Id}", new UpdateVehicleRequest(VehicleType.Car, "Blue", HasDisabledPermit: true))).ReadAsync<VehicleDto>();
        Assert.False(stillFalse.HasDisabledPermit);
        Assert.Equal("Blue", stillFalse.Color);

        var byStaff = await (await attendant.PutJsonAsync($"/api/v1/vehicles/{byDriver.Id}", new UpdateVehicleRequest(VehicleType.Car, "Blue", HasDisabledPermit: true))).ReadAsync<VehicleDto>();
        Assert.True(byStaff.HasDisabledPermit);
    }

    [Fact]
    public async Task Owner_can_update_type_and_color_but_the_plate_is_immutable()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var vehicle = await _arrange.VehicleAsync(driver);

        var updated = await (await driver.PutJsonAsync($"/api/v1/vehicles/{vehicle.Id}", new UpdateVehicleRequest(VehicleType.ElectricCar, "Silver"))).ReadAsync<VehicleDto>();

        Assert.Equal((VehicleType.ElectricCar, "Silver", vehicle.PlateNumber), (updated.Type, updated.Color, updated.PlateNumber));
    }

    [Fact]
    public async Task A_vehicle_without_history_can_be_deleted_but_one_with_history_cannot()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var fresh = await _arrange.VehicleAsync(driver);
        var parked = await _arrange.VehicleAsync(driver);
        var (_, _, spot) = await _arrange.LotWithSpotAsync(admin);
        await _arrange.StartSessionAsync(spot.Id, parked.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await driver.DeleteAsync($"/api/v1/vehicles/{fresh.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await driver.GetAsync($"/api/v1/vehicles/{fresh.Id}")).StatusCode);

        var blocked = await driver.DeleteAsync($"/api/v1/vehicles/{parked.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("vehicles.has_history", await ProblemCodeAsync(blocked));
    }

    [Fact]
    public async Task A_vehicle_cannot_have_two_active_sessions_even_when_forced_through_the_database()
    {
        // The Phase 2 unique index, seen end-to-end through the Phase 4 data.
        var admin = await _arrange.AdminAsync();
        var (_, level, spot1) = await _arrange.LotWithSpotAsync(admin);
        var spot2 = await _arrange.SpotAsync(admin, level.Id, "A-02");
        var vehicle = await _arrange.VehicleAsync(admin);
        await _arrange.StartSessionAsync(spot1.Id, vehicle.Id);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => _arrange.StartSessionAsync(spot2.Id, vehicle.Id));

        Assert.Contains("UX_Sessions_VehicleId_Active", ex.ToString());
    }
}
