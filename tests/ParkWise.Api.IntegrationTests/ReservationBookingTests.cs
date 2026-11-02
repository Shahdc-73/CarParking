using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Application.Reservations;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReservationBookingTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);

    [Fact]
    public async Task A_driver_reserves_a_spot_for_their_own_vehicle_without_blocking_it_today()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, userId) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var start = Arrange.In(TimeSpan.FromHours(3));

        var response = await Arrange.ReserveAsync(driver, lot.Id, car.Id, SpotType.Standard, start, TwoHours);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = await response.ReadAsync<ReservationDto>();
        Assert.Equal($"/api/v1/reservations/{reservation.Id}", response.Headers.Location!.ToString());
        Assert.Equal((ReservationStatus.Pending, spots[0].Id, "A-01", car.PlateNumber, userId),
            (reservation.Status, reservation.SpotId, reservation.SpotCode, reservation.PlateNumber, reservation.BookedByUserId));
        Assert.Equal(reservation.StartUtc.AddMinutes(15), reservation.HoldExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, reservation.StartUtc.Kind);

        // Booked for later, so the spot is still usable right now: a booking for next week must not block today's walk-ins.
        Assert.Equal(SpotStatus.Available, await Arrange.SpotStatusAsync(driver, spots[0].Id));
        var attendant = await _arrange.AttendantAsync();
        Assert.Equal(spots[0].Id, (await _arrange.CheckedInAsync(attendant, lot.Id)).SpotId);
    }

    [Fact]
    public async Task Drivers_cannot_book_for_other_peoples_vehicles_but_staff_can_book_for_anyone()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        var walkIn = await _arrange.VehicleAsync(attendant);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var start = Arrange.In(TimeSpan.FromHours(3));

        var stolen = await Arrange.ReserveAsync(bob, lot.Id, aliceCar.Id, SpotType.Standard, start, TwoHours);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Equal("vehicles.not_found", await ProblemCodeAsync(stolen));

        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, lot.Id, aliceCar.Id, SpotType.Standard, start, TwoHours)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, lot.Id, walkIn.Id, SpotType.Standard, start, TwoHours)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Arrange.ReserveAsync(factory.CreateRawClient(), lot.Id, aliceCar.Id, SpotType.Standard, start, TwoHours)).StatusCode);
    }

    [Fact]
    public async Task Invalid_time_windows_are_rejected_with_400()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);

        async Task<HttpResponseMessage> Book(DateTime start, TimeSpan length) => await Arrange.ReserveAsync(driver, lot.Id, car.Id, SpotType.Standard, start, length);

        var inverted = await driver.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(lot.Id, car.Id, SpotType.Standard, Arrange.In(TimeSpan.FromHours(3)), Arrange.In(TimeSpan.FromHours(2))));
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.True((await inverted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("endUtc", out _));

        Assert.Equal(HttpStatusCode.BadRequest, (await Book(Arrange.In(TimeSpan.FromHours(3)), TimeSpan.FromMinutes(29))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Book(Arrange.In(TimeSpan.FromHours(3)), TimeSpan.FromHours(25))).StatusCode);

        var past = await Book(Arrange.In(TimeSpan.FromMinutes(-10)), TwoHours);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal("reservations.start_in_past", await ProblemCodeAsync(past));

        var tooFar = await Book(Arrange.In(TimeSpan.FromDays(31)), TwoHours);
        Assert.Equal("reservations.too_far_ahead", await ProblemCodeAsync(tooFar));

        var minimum = await Book(Arrange.In(TimeSpan.FromHours(3)), TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Created, minimum.StatusCode);
    }

    [Fact]
    public async Task Unknown_lots_and_closed_lots_are_rejected()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var start = Arrange.In(TimeSpan.FromHours(3));

        Assert.Equal(HttpStatusCode.NotFound, (await Arrange.ReserveAsync(driver, Guid.NewGuid(), car.Id, SpotType.Standard, start, TwoHours)).StatusCode);

        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });
        Assert.Equal("lots.inactive", await ProblemCodeAsync(await Arrange.ReserveAsync(driver, lot.Id, car.Id, SpotType.Standard, start, TwoHours)));
    }

    [Theory]
    [InlineData(VehicleType.Car, SpotType.EV, false)]
    [InlineData(VehicleType.Car, SpotType.Compact, false)]
    [InlineData(VehicleType.Car, SpotType.Disabled, false)]
    [InlineData(VehicleType.Motorcycle, SpotType.Standard, false)]
    [InlineData(VehicleType.ElectricCar, SpotType.EV, true)]
    [InlineData(VehicleType.SmallCar, SpotType.Compact, true)]
    public async Task A_vehicle_can_only_reserve_a_compatible_spot_type(VehicleType vehicle, SpotType spotType, bool allowed)
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver, type: vehicle);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, spotType);

        var response = await Arrange.ReserveAsync(driver, lot.Id, car.Id, spotType, Arrange.In(TimeSpan.FromHours(3)), TwoHours);

        if (allowed) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("reservations.incompatible_spot_type", await ProblemCodeAsync(response));
        }
    }

    [Fact]
    public async Task Disabled_spots_can_be_reserved_only_by_permit_holders_and_motorcycles_follow_the_lot_overflow_rule()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var start = Arrange.In(TimeSpan.FromHours(3));

        var permit = (await (await attendant.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(Arrange.RandomPlate(), VehicleType.Car, "Blue", HasDisabledPermit: true))).ReadAsync<VehicleDto>());
        var (strictLot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Disabled, SpotType.Standard);
        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, strictLot.Id, permit.Id, SpotType.Disabled, start, TwoHours)).StatusCode);

        var bike = await _arrange.VehicleAsync(attendant, type: VehicleType.Motorcycle);
        Assert.Equal("reservations.incompatible_spot_type", await ProblemCodeAsync(await Arrange.ReserveAsync(attendant, strictLot.Id, bike.Id, SpotType.Standard, start, TwoHours)));

        var lenientLot = await _arrange.LotAsync(admin, overflow: true);
        var level = await _arrange.LevelAsync(admin, lenientLot.Id);
        await _arrange.SpotAsync(admin, level.Id, "A-01");
        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, lenientLot.Id, bike.Id, SpotType.Standard, start, TwoHours)).StatusCode);
    }

    // ---- overlap rules ----

    [Fact]
    public async Task Overlapping_windows_never_share_a_spot_but_back_to_back_bookings_may()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard); // ONE spot
        var v = new List<VehicleDto>();
        for (var i = 0; i < 6; i++) v.Add(await _arrange.VehicleAsync(attendant));
        var t0 = Arrange.In(TimeSpan.FromHours(4)); // far enough out that "t0 - 2h" is still in the future

        var first = await Arrange.ReservedAsync(attendant, lot.Id, v[0].Id, t0, TwoHours);                    // [t0, t0+2h)
        Assert.Equal(spots[0].Id, first.SpotId);

        async Task<string> Clash(VehicleDto vehicle, TimeSpan offset, TimeSpan length)
            => await ProblemCodeAsync(await Arrange.ReserveAsync(attendant, lot.Id, vehicle.Id, SpotType.Standard, t0 + offset, length));

        Assert.Equal("reservations.no_spot_available", await Clash(v[1], TimeSpan.FromHours(1), TwoHours));         // partial overlap at the end
        Assert.Equal("reservations.no_spot_available", await Clash(v[2], TimeSpan.FromHours(-1), TwoHours));        // partial overlap at the start
        Assert.Equal("reservations.no_spot_available", await Clash(v[3], TimeSpan.FromMinutes(30), TimeSpan.FromHours(1))); // fully inside
        Assert.Equal("reservations.no_spot_available", await Clash(v[4], TimeSpan.FromHours(-1), TimeSpan.FromHours(4)));   // fully around

        // Touching at the boundary is fine, on both sides.
        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, lot.Id, v[1].Id, SpotType.Standard, t0 + TwoHours, TwoHours)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(attendant, lot.Id, v[5].Id, SpotType.Standard, t0 - TwoHours, TwoHours)).StatusCode);
    }

    [Fact]
    public async Task Spots_are_filled_in_order_and_the_next_overlapping_booking_gets_the_next_spot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var start = Arrange.In(TimeSpan.FromHours(2));
        var a = await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, start);
        var b = await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, start);
        var c = await Arrange.ReserveAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, SpotType.Standard, start, TwoHours);

        Assert.Equal([spots[0].Id, spots[1].Id], [a.SpotId, b.SpotId]);
        Assert.Equal("reservations.no_spot_available", await ProblemCodeAsync(c));
    }

    [Fact]
    public async Task One_vehicle_cannot_hold_two_overlapping_reservations()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var t0 = Arrange.In(TimeSpan.FromHours(2));
        await Arrange.ReservedAsync(driver, lot.Id, car.Id, t0);

        var overlap = await Arrange.ReserveAsync(driver, lot.Id, car.Id, SpotType.Standard, t0 + TimeSpan.FromHours(1), TwoHours);
        Assert.Equal("reservations.vehicle_overlap", await ProblemCodeAsync(overlap));

        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(driver, lot.Id, car.Id, SpotType.Standard, t0 + TwoHours, TwoHours)).StatusCode); // later: fine
    }

    [Fact]
    public async Task Out_of_service_spots_are_never_offered_for_booking()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });

        var booking = await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, Arrange.In(TimeSpan.FromHours(2)));

        Assert.Equal(spots[1].Id, booking.SpotId);
    }

    [Fact]
    public async Task Cancelling_frees_the_slot_for_others_and_only_pending_reservations_can_be_cancelled()
    {
        var admin = await _arrange.AdminAsync();
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        var bobCar = await _arrange.VehicleAsync(bob);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard); // ONE spot
        var t0 = Arrange.In(TimeSpan.FromHours(2));
        var reservation = await Arrange.ReservedAsync(alice, lot.Id, aliceCar.Id, t0);

        Assert.Equal(HttpStatusCode.Conflict, (await Arrange.ReserveAsync(bob, lot.Id, bobCar.Id, SpotType.Standard, t0, TwoHours)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/v1/reservations/{reservation.Id}")).StatusCode); // not Bob's
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/v1/reservations/{reservation.Id}")).StatusCode);

        var kept = await (await alice.GetAsync($"/api/v1/reservations/{reservation.Id}")).ReadAsync<ReservationDto>();
        Assert.Equal(ReservationStatus.Cancelled, kept.Status);   // the record stays, for history

        Assert.Equal(HttpStatusCode.Created, (await Arrange.ReserveAsync(bob, lot.Id, bobCar.Id, SpotType.Standard, t0, TwoHours)).StatusCode);

        var again = await alice.DeleteAsync($"/api/v1/reservations/{reservation.Id}");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("reservations.not_pending", await ProblemCodeAsync(again));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/v1/reservations/{Guid.NewGuid()}")).StatusCode);
    }

    // ---- reading ----

    [Fact]
    public async Task Drivers_see_only_their_own_reservations_and_staff_see_all_with_filters_paging_and_sorting()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        var bobCar = await _arrange.VehicleAsync(bob);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var t0 = Arrange.In(TimeSpan.FromHours(2));
        var a1 = await Arrange.ReservedAsync(alice, lot.Id, aliceCar.Id, t0);
        var a2 = await Arrange.ReservedAsync(alice, lot.Id, aliceCar.Id, t0 + TimeSpan.FromHours(3));
        var b1 = await Arrange.ReservedAsync(bob, lot.Id, bobCar.Id, t0);
        await alice.DeleteAsync($"/api/v1/reservations/{a2.Id}");

        var mine = await (await alice.GetAsync("/api/v1/reservations")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Equal([a1.Id, a2.Id], mine.Items.Select(r => r.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/reservations/{b1.Id}")).StatusCode);

        async Task<PagedResult<ReservationDto>> Staff(string qs) => await (await attendant.GetAsync($"/api/v1/reservations?lotId={lot.Id}{qs}")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Equal(3, (await Staff("")).TotalCount);
        Assert.Equal([a1.Id, b1.Id], (await Staff("&status=Pending")).Items.Select(r => r.Id).Order());
        Assert.Equal([a2.Id], (await Staff("&status=Cancelled")).Items.Select(r => r.Id));
        Assert.Equal(2, (await Staff($"&vehicleId={aliceCar.Id}")).TotalCount);
        Assert.Equal(a2.Id, (await Staff("&sort=start_desc")).Items[0].Id);

        var page = await Staff("&pageSize=2&page=2");
        Assert.Equal((3, 1), (page.TotalCount, page.Items.Count));

        var window = await Staff($"&fromUtc={Uri.EscapeDataString((t0 + TimeSpan.FromHours(2.5)).ToString("O"))}");
        Assert.Equal([a2.Id], window.Items.Select(r => r.Id)); // only the late one ends after that moment
    }

    [Fact]
    public async Task Availability_counts_the_spots_still_free_for_a_window()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.EV);
        var t0 = Arrange.In(TimeSpan.FromHours(2));
        await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, t0);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[1].Id}/service", new { inService = false });

        async Task<int> Free(SpotType type, DateTime start, TimeSpan length)
        {
            var url = $"/api/v1/reservations/availability?lotId={lot.Id}&spotType={type}&startUtc={Uri.EscapeDataString(start.ToString("O"))}&endUtc={Uri.EscapeDataString((start + length).ToString("O"))}";
            return (await (await driver.GetAsync(url)).ReadAsync<ReservationAvailabilityDto>()).AvailableSpots;
        }

        Assert.Equal(1, await Free(SpotType.Standard, t0, TwoHours));                        // 3 spots - 1 out of service - 1 booked
        Assert.Equal(2, await Free(SpotType.Standard, t0 + TimeSpan.FromHours(5), TwoHours)); // the booking is over by then
        Assert.Equal(2, await Free(SpotType.Standard, t0 + TwoHours, TwoHours));              // touching is fine
        Assert.Equal(1, await Free(SpotType.EV, t0, TwoHours));

        var bad = await driver.GetAsync($"/api/v1/reservations/availability?lotId={lot.Id}&spotType=Standard&startUtc={Uri.EscapeDataString(t0.ToString("O"))}&endUtc={Uri.EscapeDataString(t0.ToString("O"))}");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await driver.GetAsync($"/api/v1/reservations/availability?lotId={Guid.NewGuid()}&spotType=Standard&startUtc={Uri.EscapeDataString(t0.ToString("O"))}&endUtc={Uri.EscapeDataString(t0.AddHours(1).ToString("O"))}")).StatusCode);
    }

    [Fact]
    public async Task Booking_and_cancelling_are_audited()
    {
        var admin = await _arrange.AdminAsync();
        var (driver, userId) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var reservation = await Arrange.ReservedAsync(driver, lot.Id, car.Id, Arrange.In(TimeSpan.FromHours(2)));
        await driver.DeleteAsync($"/api/v1/reservations/{reservation.Id}");

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={reservation.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        Assert.Equal(["Reservation.Cancelled", "Reservation.Created"], logs.Items.Select(a => a.Action));
        Assert.All(logs.Items, a => Assert.Equal(userId, a.ActorUserId));
        Assert.Contains(car.PlateNumber, logs.Items[1].AfterJson);
    }

    // ---- concurrency ----

    [Fact]
    public async Task Ten_simultaneous_bookings_of_the_same_window_for_three_spots_give_exactly_three_reservations_on_three_spots()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var vehicles = new List<VehicleDto>();
        var clients = new List<HttpClient>();
        for (var i = 0; i < 10; i++) { vehicles.Add(await _arrange.VehicleAsync(attendant)); clients.Add(await _arrange.AttendantAsync()); }
        var start = Arrange.In(TimeSpan.FromHours(2));

        var responses = await Task.WhenAll(vehicles.Select((v, i) => Arrange.ReserveAsync(clients[i], lot.Id, v.Id, SpotType.Standard, start, TwoHours)));

        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        Assert.Equal(3, created.Count);
        Assert.Equal(3, (await Task.WhenAll(created.Select(r => r.ReadAsync<ReservationDto>()))).Select(r => r.SpotId).Distinct().Count());
        foreach (var loser in responses.Except(created))
        {
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal("reservations.no_spot_available", await ProblemCodeAsync(loser));
        }
    }

    [Fact]
    public async Task Simultaneous_bookings_of_one_vehicle_create_exactly_one_reservation_even_when_spots_are_plentiful()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var car = await _arrange.VehicleAsync(attendant);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var clients = new List<HttpClient>();
        for (var i = 0; i < 6; i++) clients.Add(await _arrange.AttendantAsync());
        var start = Arrange.In(TimeSpan.FromHours(2));

        var responses = await Task.WhenAll(clients.Select(c => Arrange.ReserveAsync(c, lot.Id, car.Id, SpotType.Standard, start, TwoHours)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            Assert.Equal("reservations.vehicle_overlap", await ProblemCodeAsync(loser));

        var all = await (await attendant.GetAsync($"/api/v1/reservations?vehicleId={car.Id}")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Equal(1, all.TotalCount);
    }

    [Fact]
    public async Task Simultaneous_bookings_of_one_vehicle_on_DIFFERENT_spots_still_create_exactly_one_reservation()
    {
        // A small car fits Compact AND Standard spots. Requests for different types pick different spots, so the spot-level
        // guard cannot serialize them; only the guard on the vehicle row stops the vehicle being double-booked.
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var car = await _arrange.VehicleAsync(attendant, type: VehicleType.SmallCar);
        var lot = await _arrange.LotAsync(admin);
        var level = await _arrange.LevelAsync(admin, lot.Id);
        foreach (var i in Enumerable.Range(1, 4))
        {
            await _arrange.SpotAsync(admin, level.Id, $"C-{i:00}", SpotType.Compact);
            await _arrange.SpotAsync(admin, level.Id, $"S-{i:00}", SpotType.Standard);
        }
        var clients = new List<HttpClient>();
        for (var i = 0; i < 8; i++) clients.Add(await _arrange.AttendantAsync());
        var start = Arrange.In(TimeSpan.FromHours(2));

        var responses = await Task.WhenAll(clients.Select((c, i) =>
            Arrange.ReserveAsync(c, lot.Id, car.Id, i % 2 == 0 ? SpotType.Compact : SpotType.Standard, start, TwoHours)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var all = await (await attendant.GetAsync($"/api/v1/reservations?vehicleId={car.Id}")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Equal(1, all.TotalCount);
    }
}
