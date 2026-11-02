using System.Net;
using ParkWise.Application.Audit;
using ParkWise.Application.Common;
using ParkWise.Application.Parking;
using ParkWise.Application.Reservations;
using ParkWise.Application.Sessions;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

/// <summary>
/// The background job's behaviour over time. The job is switched off in the test host; these tests move the shared clock
/// forward and run the same maintenance service on demand. Log in / create clients BEFORE travelling.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReservationLifecycleTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    private async Task<ReservationDto> GetAsync(HttpClient client, Guid id)
        => await (await client.GetAsync($"/api/v1/reservations/{id}")).ReadAsync<ReservationDto>();

    [Fact]
    public async Task The_spot_is_held_from_the_start_time_and_walk_ins_cannot_take_it()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard); // ONE spot
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));

        await _arrange.RunMaintenanceAsync();                                    // an hour early: nothing happens
        Assert.Equal(SpotStatus.Available, await Arrange.SpotStatusAsync(attendant, spots[0].Id));

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(1)))
        {
            var result = await _arrange.RunMaintenanceAsync();

            Assert.True(result.HoldsStarted >= 1);
            Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
            var availability = await (await attendant.GetAsync($"/api/v1/lots/{lot.Id}/availability")).ReadAsync<LotAvailabilityDto>();
            Assert.Equal(new AvailabilityCounts(1, 0, 0, 1, 0), availability.Totals);

            var walkIn = await _arrange.CheckInAsync(attendant, lot.Id);        // the held spot is not for walk-ins
            Assert.Equal("sessions.no_spot_available", await ProblemCodeAsync(walkIn));
        }

        Assert.Equal(ReservationStatus.Pending, (await GetAsync(attendant, reservation.Id)).Status);
    }

    [Fact]
    public async Task A_reservation_nobody_claims_expires_15_minutes_after_the_start_and_frees_its_spot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(14)))        // still inside the hold
        {
            await _arrange.RunMaintenanceAsync();
            Assert.Equal(ReservationStatus.Pending, (await GetAsync(attendant, reservation.Id)).Status);
            Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
        }

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(16)))        // hold over
        {
            var result = await _arrange.RunMaintenanceAsync();

            Assert.True(result.Expired >= 1);
            Assert.Equal(ReservationStatus.Expired, (await GetAsync(attendant, reservation.Id)).Status);
            Assert.Equal(SpotStatus.Available, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
            Assert.Equal(HttpStatusCode.Created, (await _arrange.CheckInAsync(attendant, lot.Id)).StatusCode); // free for walk-ins again

            // The job is idempotent: a second run changes nothing about this reservation.
            await _arrange.RunMaintenanceAsync();
            Assert.Equal(ReservationStatus.Expired, (await GetAsync(attendant, reservation.Id)).Status);
        }

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={reservation.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        var expired = logs.Items.Single(a => a.Action == "Reservation.Expired");
        Assert.Null(expired.ActorUserId);   // done by the system, not by a person
    }

    [Fact]
    public async Task An_expired_reservation_cannot_be_cancelled_or_claimed()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));

        using var _ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(20));
        await _arrange.RunMaintenanceAsync();

        Assert.Equal("reservations.not_pending", await ProblemCodeAsync(await attendant.DeleteAsync($"/api/v1/reservations/{reservation.Id}")));
        var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);
        Assert.Null(session.ReservationId);       // arrived too late: treated as an ordinary walk-in
    }

    [Fact]
    public async Task The_reserved_vehicle_claims_its_spot_during_the_hold_and_the_job_leaves_it_alone_afterwards()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(5)))
        {
            await _arrange.RunMaintenanceAsync();
            Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[0].Id));

            var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);

            Assert.Equal((spots[0].Id, reservation.Id), (session.SpotId, session.ReservationId));
            Assert.Equal(SpotStatus.Occupied, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
            Assert.Equal(ReservationStatus.Fulfilled, (await GetAsync(attendant, reservation.Id)).Status);
        }

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(30)))        // long after the hold would have ended
        {
            await _arrange.RunMaintenanceAsync();
            Assert.Equal(ReservationStatus.Fulfilled, (await GetAsync(attendant, reservation.Id)).Status); // not expired
            Assert.Equal(SpotStatus.Occupied, await Arrange.SpotStatusAsync(attendant, spots[0].Id));       // not released under the car
        }
    }

    [Fact]
    public async Task A_vehicle_may_arrive_up_to_15_minutes_early_and_gets_the_spot_it_reserved()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var walkIn = await _arrange.CheckedInAsync(attendant, lot.Id);                       // takes A-01
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(TimeSpan.FromMinutes(10)));
        Assert.Equal(spots[1].Id, reservation.SpotId);                                       // A-01 is busy, so A-02
        await Arrange.CheckOutAsync(attendant, walkIn.Id);                                   // A-01 is free (and lower) again

        var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);

        // A normal check-in would pick A-01; the reservation sends the car to the spot it booked.
        Assert.Equal((spots[1].Id, reservation.Id), (session.SpotId, session.ReservationId));
        Assert.Equal(ReservationStatus.Fulfilled, (await GetAsync(attendant, reservation.Id)).Status);
        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={session.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Contains(reservation.Id.ToString(), logs.Items[0].AfterJson);
    }

    [Fact]
    public async Task Arriving_much_too_early_is_an_ordinary_check_in_and_the_reservation_stays_pending()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(TimeSpan.FromHours(3)));

        var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);

        Assert.Null(session.ReservationId);
        Assert.Equal(spots[0].Id, session.SpotId);
        Assert.Equal(ReservationStatus.Pending, (await GetAsync(attendant, reservation.Id)).Status);
    }

    [Fact]
    public async Task A_reservation_only_applies_in_its_own_lot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lotA, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var (lotB, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        await Arrange.ReservedAsync(attendant, lotA.Id, car.Id, Arrange.In(TimeSpan.FromMinutes(5)));

        var elsewhere = await _arrange.CheckedInAsync(attendant, lotB.Id, car.PlateNumber, type: null);

        Assert.Null(elsewhere.ReservationId);
    }

    [Fact]
    public async Task If_a_walk_in_is_still_parked_in_the_reserved_spot_the_booking_moves_to_another_free_spot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));
        Assert.Equal(spots[0].Id, reservation.SpotId);
        var squatter = await _arrange.CheckedInAsync(attendant, lot.Id);                       // the hold has not started, so A-01 is fair game
        Assert.Equal(spots[0].Id, squatter.SpotId);

        using (factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(1)))
        {
            var result = await _arrange.RunMaintenanceAsync();

            Assert.True(result.Reassigned >= 1);
            var moved = await GetAsync(attendant, reservation.Id);
            Assert.Equal((ReservationStatus.Pending, spots[1].Id), (moved.Status, moved.SpotId));
            Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[1].Id));
            Assert.Equal(SpotStatus.Occupied, await Arrange.SpotStatusAsync(attendant, spots[0].Id));  // the squatter is untouched

            var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);
            Assert.Equal((spots[1].Id, reservation.Id), (session.SpotId, session.ReservationId));
        }

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={reservation.Id}&action=Reservation.Reassigned")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Single(logs.Items);
    }

    [Fact]
    public async Task If_no_other_spot_is_free_when_the_hold_begins_the_reservation_is_cancelled_and_audited()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard); // ONE spot
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));
        await _arrange.CheckedInAsync(attendant, lot.Id);                              // walk-in takes the only spot

        using var _ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(1));
        var result = await _arrange.RunMaintenanceAsync();

        Assert.True(result.CancelledNoSpot >= 1);
        Assert.Equal(ReservationStatus.Cancelled, (await GetAsync(attendant, reservation.Id)).Status);
        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={reservation.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        Assert.Contains(logs.Items, a => a.Action == "Reservation.CancelledNoSpot" && a.ActorUserId is null);
    }

    [Fact]
    public async Task A_spot_taken_out_of_service_after_booking_is_replaced_when_the_hold_begins()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(OneHour));
        Assert.Equal(spots[0].Id, reservation.SpotId);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });

        using var _ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(1));
        await _arrange.RunMaintenanceAsync();

        Assert.Equal(spots[1].Id, (await GetAsync(attendant, reservation.Id)).SpotId);
        Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[1].Id));
    }

    [Fact]
    public async Task Cancelling_during_the_hold_releases_the_spot_immediately()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var car = await _arrange.VehicleAsync(driver);
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var reservation = await Arrange.ReservedAsync(driver, lot.Id, car.Id, Arrange.In(OneHour));

        using var __ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(5));
        await _arrange.RunMaintenanceAsync();
        Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[0].Id));

        Assert.Equal(HttpStatusCode.NoContent, (await driver.DeleteAsync($"/api/v1/reservations/{reservation.Id}")).StatusCode);

        Assert.Equal(SpotStatus.Available, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
        Assert.Equal(HttpStatusCode.Created, (await _arrange.CheckInAsync(attendant, lot.Id)).StatusCode);
    }

    [Fact]
    public async Task A_spot_that_is_being_held_cannot_be_taken_out_of_service()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, Arrange.In(OneHour));

        using var _ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(1));
        await _arrange.RunMaintenanceAsync();

        var response = await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Back_to_back_bookings_on_one_spot_hand_over_cleanly_in_a_single_job_run()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard); // ONE spot
        var first = await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, Arrange.In(OneHour), TimeSpan.FromMinutes(30));
        var second = await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, first.EndUtc, OneHour);
        Assert.Equal(spots[0].Id, second.SpotId);

        // The first booking's hold ended long ago and the second one's has just begun: one run settles both.
        using var _ = factory.Clock.TravelBy(OneHour + TimeSpan.FromMinutes(31));
        await _arrange.RunMaintenanceAsync();

        Assert.Equal(ReservationStatus.Expired, (await GetAsync(attendant, first.Id)).Status);
        Assert.Equal(ReservationStatus.Pending, (await GetAsync(attendant, second.Id)).Status);
        Assert.Equal(SpotStatus.Reserved, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
    }

    [Fact]
    public async Task A_session_started_from_a_reservation_checks_out_and_is_billed_like_any_other()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(TimeSpan.FromMinutes(5)));
        var session = await _arrange.CheckedInAsync(attendant, lot.Id, car.PlateNumber, type: null);

        ReceiptDto receipt;
        using (factory.Clock.TravelBy(TimeSpan.FromHours(2)))
            receipt = await (await Arrange.CheckOutAsync(attendant, session.Id)).ReadAsync<ReceiptDto>();

        Assert.Equal(6.00m, receipt.Amount);
        Assert.Equal(SpotStatus.Available, await Arrange.SpotStatusAsync(attendant, spots[0].Id));
    }

    [Fact]
    public async Task Running_the_job_when_there_is_nothing_to_do_is_harmless()
    {
        var result = await _arrange.RunMaintenanceAsync();
        Assert.True(result.Expired >= 0 && result.HoldsStarted >= 0);
        var again = await _arrange.RunMaintenanceAsync();
        Assert.Equal(0, again.Expired + again.Reassigned + again.CancelledNoSpot);
    }
}
