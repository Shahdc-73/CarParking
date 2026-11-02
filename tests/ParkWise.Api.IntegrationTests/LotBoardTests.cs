using System.Net;
using ParkWise.Application.Parking;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LotBoardTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    private static Task<HttpResponseMessage> Get(HttpClient client, Guid lotId) => client.GetAsync($"/api/v1/lots/{lotId}/board");

    private static async Task<LotBoardDto> BoardAsync(HttpClient client, Guid lotId)
        => await (await Get(client, lotId)).ReadAsync<LotBoardDto>();

    private static BoardSpotDto SpotOf(LotBoardDto board, string code) => board.Levels.SelectMany(l => l.Spots).Single(s => s.Code == code);

    [Fact]
    public async Task The_board_lists_levels_in_order_with_their_spots_sorted_by_code_and_matching_totals()
    {
        var admin = await _arrange.AdminAsync();
        var lot = await _arrange.LotAsync(admin);
        var upper = await _arrange.LevelAsync(admin, lot.Id, 2);
        var ground = await _arrange.LevelAsync(admin, lot.Id, 1);
        await _arrange.LevelAsync(admin, lot.Id, 3); // an empty level still appears
        await _arrange.SpotAsync(admin, ground.Id, "B-02");
        await _arrange.SpotAsync(admin, ground.Id, "B-01", SpotType.EV);
        await _arrange.SpotAsync(admin, upper.Id, "C-01");

        var board = await BoardAsync(admin, lot.Id);

        Assert.Equal(lot.Name, board.LotName);
        Assert.Equal([1, 2, 3], board.Levels.Select(l => l.Number));
        Assert.Equal(["B-01", "B-02"], board.Levels[0].Spots.Select(s => s.Code));
        Assert.Equal(SpotType.EV, board.Levels[0].Spots[0].Type);
        Assert.Empty(board.Levels[2].Spots);
        Assert.Equal(new AvailabilityCounts(3, 3, 0, 0, 0), board.Totals);
        Assert.Equal(DateTimeKind.Utc, board.GeneratedAtUtc.Kind);
        Assert.All(board.Levels.SelectMany(l => l.Spots), s => Assert.Equal(SpotStatus.Available, s.Status));
    }

    [Fact]
    public async Task Staff_see_who_is_parked_since_when_and_the_fee_so_far()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var plate = Arrange.RandomPlate();
        var session = await _arrange.CheckedInAsync(attendant, lot.Id, plate);

        // Straight after check-in: inside the free period, so the fee is 0.00.
        var fresh = SpotOf(await BoardAsync(attendant, lot.Id), "A-01");
        Assert.Equal(SpotStatus.Occupied, fresh.Status);
        Assert.NotNull(fresh.Occupancy);
        Assert.Equal((session.Id, plate, 0.00m, 15, false), (fresh.Occupancy.SessionId, fresh.Occupancy.PlateNumber, fresh.Occupancy.FeeSoFar, fresh.Occupancy.FreeMinutes, fresh.Occupancy.IsMine));
        Assert.Equal(session.CheckInAtUtc, fresh.Occupancy.CheckInAtUtc);
        Assert.Equal(SpotStatus.Available, SpotOf(await BoardAsync(attendant, lot.Id), "A-02").Status);
        Assert.Null(SpotOf(await BoardAsync(attendant, lot.Id), "A-02").Occupancy);

        // Two hours later: 1h45 billable -> 2 started hours at 3.00.
        using (factory.Clock.TravelBy(TimeSpan.FromHours(2)))
        {
            var later = await BoardAsync(attendant, lot.Id);
            Assert.Equal(6.00m, SpotOf(later, "A-01").Occupancy!.FeeSoFar);
            Assert.True(later.GeneratedAtUtc > fresh.Occupancy.CheckInAtUtc.AddHours(1.9));   // the server clock the browser corrects against
        }
    }

    [Fact]
    public async Task The_fee_so_far_uses_the_price_of_the_spots_own_type()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.EV);
        await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.ElectricCar);

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(2));
        var board = await BoardAsync(attendant, lot.Id);

        Assert.Equal(7.00m, SpotOf(board, "A-01").Occupancy!.FeeSoFar); // EV: 3.50 an hour
    }

    [Fact]
    public async Task Each_parked_car_is_priced_by_its_own_spot_type_when_several_types_are_occupied_at_once()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.EV);
        await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.Car);           // A-01 Standard: 3.00 an hour
        await _arrange.CheckedInAsync(attendant, lot.Id, type: VehicleType.ElectricCar);   // A-02 EV: 3.50 an hour

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(2));
        var board = await BoardAsync(attendant, lot.Id);

        Assert.Equal(6.00m, SpotOf(board, "A-01").Occupancy!.FeeSoFar);
        Assert.Equal(7.00m, SpotOf(board, "A-02").Occupancy!.FeeSoFar);
    }

    [Fact]
    public async Task The_fee_so_far_matches_what_check_out_then_charges()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var session = await _arrange.CheckedInAsync(attendant, lot.Id);

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(10));
        var shown = SpotOf(await BoardAsync(attendant, lot.Id), "A-01").Occupancy!.FeeSoFar;
        var charged = await (await Arrange.CheckOutAsync(attendant, session.Id)).ReadAsync<ParkWise.Application.Sessions.ReceiptDto>();

        Assert.Equal(20.00m, shown);      // capped
        Assert.Equal(charged.Amount, shown);
    }

    [Fact]
    public async Task A_driver_sees_other_cars_as_occupied_and_nothing_more_but_sees_their_own_in_full()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var alicesSession = await _arrange.CheckedInAsync(attendant, lot.Id, aliceCar.PlateNumber, type: null); // A-01
        var walkIn = await _arrange.CheckedInAsync(attendant, lot.Id);                                          // A-02

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(2));
        var aliceView = await BoardAsync(alice, lot.Id);
        var bobView = await BoardAsync(bob, lot.Id);

        // Alice: her own car in full, the stranger's car only as "occupied".
        var mine = SpotOf(aliceView, "A-01").Occupancy!;
        Assert.Equal((alicesSession.Id, aliceCar.PlateNumber, 6.00m, true), (mine.SessionId, mine.PlateNumber, mine.FeeSoFar, mine.IsMine));
        Assert.Equal(SpotStatus.Occupied, SpotOf(aliceView, "A-02").Status);
        Assert.Null(SpotOf(aliceView, "A-02").Occupancy);

        // Bob owns nothing here: he sees two red spots and no details at all.
        Assert.Equal(SpotStatus.Occupied, SpotOf(bobView, "A-01").Status);
        Assert.Null(SpotOf(bobView, "A-01").Occupancy);
        Assert.Null(SpotOf(bobView, "A-02").Occupancy);
        Assert.Equal(2, bobView.Totals.Occupied);

        // The stranger's plate must not appear ANYWHERE in what a driver receives.
        var raw = await (await Get(bob, lot.Id)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(walkIn.PlateNumber, raw);
        Assert.DoesNotContain(aliceCar.PlateNumber, raw);
        Assert.DoesNotContain(walkIn.TicketCode, raw);
    }

    [Fact]
    public async Task Out_of_service_spots_are_marked_and_a_checked_out_spot_goes_back_to_available()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[1].Id}/service", new { inService = false });
        var session = await _arrange.CheckedInAsync(attendant, lot.Id);

        var busy = await BoardAsync(attendant, lot.Id);
        Assert.Equal(SpotStatus.OutOfService, SpotOf(busy, "A-02").Status);
        Assert.Equal(new AvailabilityCounts(2, 0, 1, 0, 1), busy.Totals);

        await Arrange.CheckOutAsync(attendant, session.Id);
        var after = await BoardAsync(attendant, lot.Id);
        Assert.Equal(SpotStatus.Available, SpotOf(after, "A-01").Status);
        Assert.Null(SpotOf(after, "A-01").Occupancy);
    }

    [Fact]
    public async Task A_spot_held_for_a_reservation_shows_when_the_hold_ends_to_staff_only()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(TimeSpan.FromHours(1)));

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2));
        await _arrange.RunMaintenanceAsync();

        var staff = SpotOf(await BoardAsync(attendant, lot.Id), "A-01");
        Assert.Equal(SpotStatus.Reserved, staff.Status);
        Assert.Equal(reservation.HoldExpiresAtUtc, staff.HoldExpiresAtUtc);

        var asDriver = SpotOf(await BoardAsync(driver, lot.Id), "A-01");
        Assert.Equal(SpotStatus.Reserved, asDriver.Status);
        Assert.Null(asDriver.HoldExpiresAtUtc);
    }

    [Fact]
    public async Task Unknown_lots_return_404_and_anonymous_users_are_refused()
    {
        var attendant = await _arrange.AttendantAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Get(attendant, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(factory.CreateRawClient(), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task The_seeded_lot_board_matches_the_seed()
    {
        var (driver, _) = await _arrange.NewDriverAsync();
        var lots = await (await driver.GetAsync("/api/v1/lots?search=Downtown")).ReadAsync<ParkWise.Application.Common.PagedResult<LotDto>>();

        var board = await BoardAsync(driver, lots.Items.Single().Id);

        Assert.Equal(["Ground", "Level 2", "Level 3"], board.Levels.Select(l => l.Name));
        Assert.Equal([12, 12, 8], board.Levels.Select(l => l.Spots.Count));
        Assert.Equal(32, board.Totals.Total);
    }
}
