using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Application.Sessions;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SessionCheckInTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Check_in_assigns_a_spot_issues_a_ticket_and_occupies_the_spot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var plate = Arrange.RandomPlate();

        var response = await _arrange.CheckInAsync(attendant, lot.Id, plate.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.ReadAsync<SessionDto>();
        Assert.Equal($"/api/v1/sessions/{session.Id}", response.Headers.Location!.ToString());
        Assert.Matches("^PW-[A-HJKMNP-TV-Z2-9]{6}$", session.TicketCode);
        Assert.Equal((SessionStatus.Active, plate, spots[0].Id, "A-01"), (session.Status, session.PlateNumber, session.SpotId, session.SpotCode));
        Assert.Equal(DateTimeKind.Utc, session.CheckInAtUtc.Kind);
        Assert.Null(session.CheckOutAtUtc);
        Assert.Null(session.ReceiptId);

        var spot = await (await attendant.GetAsync($"/api/v1/spots/{spots[0].Id}")).ReadAsync<SpotDto>();
        Assert.Equal(SpotStatus.Occupied, spot.Status);

        var availability = await (await attendant.GetAsync($"/api/v1/lots/{lot.Id}/availability")).ReadAsync<LotAvailabilityDto>();
        Assert.Equal(new AvailabilityCounts(1, 0, 1, 0, 0), availability.Totals);
    }

    [Fact]
    public async Task An_unknown_plate_becomes_a_walk_in_vehicle_but_needs_a_type()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var plate = Arrange.RandomPlate();

        var missingType = await _arrange.CheckInAsync(attendant, lot.Id, plate, type: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingType.StatusCode);
        Assert.Equal("vehicles.type_required", await ProblemCodeAsync(missingType));

        await _arrange.CheckedInAsync(attendant, lot.Id, plate, VehicleType.Car);
        var vehicle = await (await attendant.GetAsync($"/api/v1/vehicles/lookup?plate={plate}")).ReadAsync<VehicleDto>();
        Assert.Null(vehicle.OwnerUserId);
        Assert.Equal(VehicleType.Car, vehicle.Type);
    }

    [Fact]
    public async Task A_registered_vehicle_checks_in_by_plate_alone()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (driver, userId) = await _arrange.NewDriverAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(driver);

        var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);

        Assert.Equal(car.Id, session.VehicleId);
        var mine = await (await driver.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<SessionDto>>();
        Assert.Equal([session.Id], mine.Items.Select(s => s.Id)); // the owner sees it in their history
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task The_same_vehicle_cannot_be_parked_twice()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var plate = Arrange.RandomPlate();
        await _arrange.CheckedInAsync(attendant, lot.Id, plate);

        var second = await _arrange.CheckInAsync(attendant, lot.Id, $"{plate[..3]}-{plate[3..]}"); // same plate, different formatting

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("sessions.vehicle_already_parked", await ProblemCodeAsync(second));
    }

    [Fact]
    public async Task A_vehicle_can_check_in_again_after_leaving()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var plate = Arrange.RandomPlate();
        var first = await _arrange.CheckedInAsync(attendant, lot.Id, plate);
        Assert.Equal(HttpStatusCode.OK, (await Arrange.CheckOutAsync(attendant, first.Id)).StatusCode);

        var again = await _arrange.CheckInAsync(attendant, lot.Id, plate);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.NotEqual(first.TicketCode, (await again.ReadAsync<SessionDto>()).TicketCode);
    }

    [Fact]
    public async Task Spots_are_assigned_lowest_level_then_lowest_code_first()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var lot = await _arrange.LotAsync(admin);
        var upper = await _arrange.LevelAsync(admin, lot.Id, 2);
        var ground = await _arrange.LevelAsync(admin, lot.Id, 1);
        await _arrange.SpotAsync(admin, upper.Id, "A-01");
        await _arrange.SpotAsync(admin, ground.Id, "B-02");
        await _arrange.SpotAsync(admin, ground.Id, "B-01");

        var first = await _arrange.CheckedInAsync(attendant, lot.Id);
        var second = await _arrange.CheckedInAsync(attendant, lot.Id);
        var third = await _arrange.CheckedInAsync(attendant, lot.Id);

        Assert.Equal([(1, "B-01"), (1, "B-02"), (2, "A-01")],
            [(first.LevelNumber, first.SpotCode), (second.LevelNumber, second.SpotCode), (third.LevelNumber, third.SpotCode)]);
    }

    [Theory]
    [InlineData(VehicleType.SmallCar, SpotType.Compact)]
    [InlineData(VehicleType.ElectricCar, SpotType.EV)]
    public async Task Vehicles_get_their_best_fitting_spot_type_before_falling_back_to_standard(VehicleType vehicleType, SpotType preferred)
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        // Standard has the lowest code, so only the preference rule can send the vehicle to the other spot.
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, preferred, SpotType.Standard);

        var first = await _arrange.CheckedInAsync(attendant, lot.Id, type: vehicleType);
        var second = await _arrange.CheckedInAsync(attendant, lot.Id, type: vehicleType);

        Assert.Equal(preferred, first.SpotType);
        Assert.Equal(SpotType.Standard, second.SpotType); // preferred type is full, so it falls back
    }

    [Fact]
    public async Task A_car_never_takes_a_motorcycle_ev_or_disabled_spot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Motorcycle, SpotType.EV, SpotType.Disabled, SpotType.Compact);

        var response = await _arrange.CheckInAsync(attendant, lot.Id, type: VehicleType.Car);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sessions.no_spot_available", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Motorcycles_overflow_into_car_spots_only_when_the_lot_allows_it()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();

        var strict = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Compact);
        var denied = await _arrange.CheckInAsync(attendant, strict.Lot.Id, type: VehicleType.Motorcycle);
        Assert.Equal("sessions.no_spot_available", await ProblemCodeAsync(denied));

        var lenientLot = await _arrange.LotAsync(admin, overflow: true);
        var level = await _arrange.LevelAsync(admin, lenientLot.Id);
        await _arrange.SpotAsync(admin, level.Id, "A-01", SpotType.Standard);
        await _arrange.SpotAsync(admin, level.Id, "A-02", SpotType.Compact);
        var allowed = await _arrange.CheckedInAsync(attendant, lenientLot.Id, type: VehicleType.Motorcycle);
        Assert.Equal(SpotType.Compact, allowed.SpotType); // best fit among the overflow options
    }

    [Fact]
    public async Task Disabled_spots_are_for_permit_holders_only_and_they_get_them_first()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Disabled);

        var holder = await attendant.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(Arrange.RandomPlate(), VehicleType.Car, "Blue", HasDisabledPermit: true));
        var holderPlate = (await holder.ReadAsync<VehicleDto>()).PlateNumber;

        var withPermit = await _arrange.CheckedInAsync(attendant, lot.Id, holderPlate, type: null);
        var withoutPermit = await _arrange.CheckedInAsync(attendant, lot.Id);

        Assert.Equal(SpotType.Disabled, withPermit.SpotType);
        Assert.Equal(SpotType.Standard, withoutPermit.SpotType);

        var third = await _arrange.CheckInAsync(attendant, lot.Id); // only a Disabled spot... which is taken, and not theirs anyway
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task Out_of_service_and_occupied_spots_are_never_assigned()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });

        var only = await _arrange.CheckedInAsync(attendant, lot.Id);
        Assert.Equal(spots[1].Id, only.SpotId);

        var full = await _arrange.CheckInAsync(attendant, lot.Id);
        Assert.Equal("sessions.no_spot_available", await ProblemCodeAsync(full));
    }

    [Fact]
    public async Task A_spot_returned_to_service_can_be_assigned_again()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });
        Assert.Equal(HttpStatusCode.Conflict, (await _arrange.CheckInAsync(attendant, lot.Id)).StatusCode);

        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = true });

        Assert.Equal(HttpStatusCode.Created, (await _arrange.CheckInAsync(attendant, lot.Id)).StatusCode);
    }

    [Fact]
    public async Task Check_in_to_an_unknown_or_closed_lot_is_rejected()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);

        Assert.Equal(HttpStatusCode.NotFound, (await _arrange.CheckInAsync(attendant, Guid.NewGuid(), Arrange.RandomPlate())).StatusCode);

        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });
        var closed = await _arrange.CheckInAsync(attendant, lot.Id);
        Assert.Equal("lots.inactive", await ProblemCodeAsync(closed));
    }

    [Fact]
    public async Task Invalid_check_in_requests_return_400_with_field_errors()
    {
        var attendant = await _arrange.AttendantAsync();

        var response = await attendant.PostAsync("/api/v1/sessions/check-in",
            new StringContent("""{"lotId":"00000000-0000-0000-0000-000000000000","plateNumber":"!","vehicleType":"Hovercraft"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var bad = await attendant.PostJsonAsync("/api/v1/sessions/check-in", new CheckInRequest(Guid.Empty, "!", null));
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lotId", out _));
        Assert.True(errors.TryGetProperty("plateNumber", out _));
    }

    [Fact]
    public async Task Only_staff_can_check_vehicles_in()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var (driver, _) = await _arrange.NewDriverAsync();
        var body = new CheckInRequest(lot.Id, Arrange.RandomPlate(), VehicleType.Car);

        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostJsonAsync("/api/v1/sessions/check-in", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().PostJsonAsync("/api/v1/sessions/check-in", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostJsonAsync("/api/v1/sessions/check-in", body)).StatusCode);
    }

    [Fact]
    public async Task Check_in_is_audited_with_the_attendant_as_actor()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var me = await (await attendant.GetAsync("/api/v1/auth/me")).ReadAsync<ParkWise.Application.Auth.UserDto>();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var session = await _arrange.CheckedInAsync(attendant, lot.Id);

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={session.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        var entry = Assert.Single(logs.Items);
        Assert.Equal(("Session.CheckedIn", me.Id), (entry.Action, entry.ActorUserId));
        Assert.Contains(session.TicketCode, entry.AfterJson);
    }

    // ---- concurrency: the point of the phase ----

    [Fact]
    public async Task Ten_simultaneous_check_ins_for_three_spots_give_exactly_three_sessions_on_three_different_spots()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var gates = new List<HttpClient>();
        for (var i = 0; i < 10; i++) gates.Add(await _arrange.AttendantAsync());

        var responses = await Task.WhenAll(gates.Select(g => _arrange.CheckInAsync(g, lot.Id, Arrange.RandomPlate())));

        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        Assert.Equal(3, created.Count);
        var sessions = await Task.WhenAll(created.Select(r => r.ReadAsync<SessionDto>()));
        Assert.Equal(3, sessions.Select(s => s.SpotId).Distinct().Count()); // nobody shares a spot

        foreach (var loser in responses.Except(created))
        {
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal("sessions.no_spot_available", await ProblemCodeAsync(loser));
        }

        var availability = await (await admin.GetAsync($"/api/v1/lots/{lot.Id}/availability")).ReadAsync<LotAvailabilityDto>();
        Assert.Equal(new AvailabilityCounts(3, 0, 3, 0, 0), availability.Totals);
    }

    [Fact]
    public async Task Simultaneous_check_ins_of_a_new_plate_create_exactly_one_session()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var plate = Arrange.RandomPlate();
        var gates = new List<HttpClient>();
        for (var i = 0; i < 6; i++) gates.Add(await _arrange.AttendantAsync());

        var responses = await Task.WhenAll(gates.Select(g => _arrange.CheckInAsync(g, lot.Id, plate)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            Assert.Equal("sessions.vehicle_already_parked", await ProblemCodeAsync(loser));

        // exactly one spot is occupied
        var availability = await (await admin.GetAsync($"/api/v1/lots/{lot.Id}/availability")).ReadAsync<LotAvailabilityDto>();
        Assert.Equal(1, availability.Totals.Occupied);
    }

    [Fact]
    public async Task Simultaneous_check_ins_of_an_already_registered_vehicle_create_exactly_one_session()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var gates = new List<HttpClient>();
        for (var i = 0; i < 4; i++) gates.Add(await _arrange.AttendantAsync());

        var responses = await Task.WhenAll(gates.Select(g => _arrange.CheckInAsync(g, lot.Id, car.PlateNumber, type: null)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            Assert.Equal("sessions.vehicle_already_parked", await ProblemCodeAsync(loser));
    }
}
