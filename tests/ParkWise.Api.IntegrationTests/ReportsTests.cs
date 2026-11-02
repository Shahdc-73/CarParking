using System.Net;
using ParkWise.Application.Reports;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReportsTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static string Url(Guid? lotId, string from, string to, int offset = 0)
        => $"/api/v1/reports/performance?from={from}&to={to}&utcOffsetMinutes={offset}" + (lotId is null ? "" : $"&lotId={lotId}");

    private static async Task<PerformanceReportDto> PerformanceAsync(HttpClient admin, Guid? lotId, string from, string to, int offset = 0)
        => await (await admin.GetAsync(Url(lotId, from, to, offset))).ReadAsync<PerformanceReportDto>();

    /// <summary>A fresh lot with one spot and one vehicle, so a test's history is isolated from every other test's.</summary>
    private async Task<(Guid LotId, Guid SpotId, Guid VehicleId)> ArenaAsync(HttpClient admin)
    {
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var vehicle = await _arrange.VehicleAsync(admin);
        return (lot.Id, spots[0].Id, vehicle.Id);
    }

    [Fact]
    public async Task Revenue_is_summed_per_day_with_empty_days_included()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, spot, vehicle) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 8), Utc(3, 10, 10), 6.00m);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 12), Utc(3, 10, 15), 9.00m);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 12, 9), Utc(3, 12, 20), 20.00m);

        var report = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-13");

        Assert.Equal(35.00m, report.TotalRevenue);
        Assert.Equal(3, report.PaidSessions);
        Assert.Equal(new[] { 15.00m, 0m, 20.00m, 0m }, report.RevenueByDay.Select(d => d.Amount));
        Assert.Equal(new[] { 2, 0, 1, 0 }, report.RevenueByDay.Select(d => d.Sessions));
        Assert.Equal(new DateOnly(2026, 3, 10), report.RevenueByDay[0].Date);
    }

    [Fact]
    public async Task The_viewers_utc_offset_decides_which_day_a_late_night_payment_belongs_to()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, spot, vehicle) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 21), Utc(3, 10, 23, 30), 10.00m); // paid 23:30 UTC

        var utc = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-11", offset: 0);
        var plus2 = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-11", offset: 120);

        Assert.Equal(new[] { 10.00m, 0m }, utc.RevenueByDay.Select(d => d.Amount));     // still March 10 in UTC
        Assert.Equal(new[] { 0m, 10.00m }, plus2.RevenueByDay.Select(d => d.Amount));   // already March 11 in UTC+2
    }

    [Fact]
    public async Task Payments_outside_the_chosen_days_are_excluded()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, spot, vehicle) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 9, 8), Utc(3, 9, 23, 59), 5.00m);   // the day before
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 8), Utc(3, 10, 9), 7.00m);      // inside
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 11, 0), Utc(3, 11, 1), 11.00m);     // the day after

        var report = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-10");

        Assert.Equal(7.00m, report.TotalRevenue);
        Assert.Single(report.RevenueByDay);
    }

    [Fact]
    public async Task Average_and_longest_stay_and_lost_tickets_are_counted_from_closed_sessions()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, spot, vehicle) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 8), Utc(3, 10, 9), 3.00m);                    // 60 min
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 8), Utc(3, 10, 11), 9.00m);                   // 180 min
        await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, 10, 8), Utc(3, 10, 8, 30), 50.00m, lostTicket: true); // 30 min, lost ticket

        var report = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-10");

        Assert.Equal(90.0, report.AverageStayMinutes);   // (60 + 180 + 30) / 3
        Assert.Equal(180.0, report.LongestStayMinutes);
        Assert.Equal(1, report.LostTickets);
        Assert.Equal(62.00m, report.TotalRevenue);       // lost-ticket fees are revenue too
    }

    [Fact]
    public async Task Peak_hours_count_arrivals_per_local_hour_and_always_list_all_24()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, spot, vehicle) = await ArenaAsync(admin);
        foreach (var (day, hour) in new[] { (10, 8), (10, 8), (11, 8), (10, 17), (11, 23) })
            await _arrange.AddHistoryAsync(spot, vehicle, Utc(3, day, hour, 15), Utc(3, day, hour, 45), 0m);

        var utc = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-11", offset: 0);
        Assert.Equal(24, utc.PeakHours.Count);
        Assert.Equal(3, utc.PeakHours[8].CheckIns);
        Assert.Equal(1, utc.PeakHours[17].CheckIns);
        Assert.Equal(1, utc.PeakHours[23].CheckIns);
        Assert.Equal(5, utc.CheckIns);

        var plus2 = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-12", offset: 120);
        Assert.Equal(3, plus2.PeakHours[10].CheckIns);   // 08:15 UTC is 10:15 at UTC+2
        Assert.Equal(1, plus2.PeakHours[1].CheckIns);    // 23:15 UTC wraps to 01:15 the next day
    }

    [Fact]
    public async Task A_lot_filter_keeps_other_lots_out_of_the_numbers()
    {
        var admin = await _arrange.AdminAsync();
        var (lotA, spotA, vehicleA) = await ArenaAsync(admin);
        var (lotB, spotB, vehicleB) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spotA, vehicleA, Utc(3, 10, 8), Utc(3, 10, 10), 6.00m);
        await _arrange.AddHistoryAsync(spotB, vehicleB, Utc(3, 10, 8), Utc(3, 10, 10), 100.00m);

        Assert.Equal(6.00m, (await PerformanceAsync(admin, lotA, "2026-03-10", "2026-03-10")).TotalRevenue);
        Assert.Equal(100.00m, (await PerformanceAsync(admin, lotB, "2026-03-10", "2026-03-10")).TotalRevenue);
    }

    [Fact]
    public async Task Without_a_lot_filter_all_lots_are_included()
    {
        var admin = await _arrange.AdminAsync();
        var (_, spotA, vehicleA) = await ArenaAsync(admin);
        var (_, spotB, vehicleB) = await ArenaAsync(admin);
        await _arrange.AddHistoryAsync(spotA, vehicleA, Utc(1, 2, 8), Utc(1, 2, 10), 6.00m);
        await _arrange.AddHistoryAsync(spotB, vehicleB, Utc(1, 2, 8), Utc(1, 2, 10), 100.00m);

        var report = await PerformanceAsync(admin, null, "2026-01-02", "2026-01-02");

        Assert.True(report.TotalRevenue >= 106.00m);
    }

    [Fact]
    public async Task A_range_with_no_activity_returns_zeros_and_no_averages_instead_of_failing()
    {
        var admin = await _arrange.AdminAsync();
        var (lotId, _, _) = await ArenaAsync(admin);

        var report = await PerformanceAsync(admin, lotId, "2026-03-10", "2026-03-16");

        Assert.Equal((0m, 0, 0, 0), (report.TotalRevenue, report.PaidSessions, report.LostTickets, report.CheckIns));
        Assert.Null(report.AverageStayMinutes);
        Assert.Null(report.LongestStayMinutes);
        Assert.Equal(7, report.RevenueByDay.Count);
        Assert.Equal(24, report.PeakHours.Count);
    }

    [Theory]
    [InlineData("2026-03-10", "2026-03-01", 0)]    // end before start
    [InlineData("2026-01-01", "2026-04-03", 0)]    // 93 days
    [InlineData("2026-03-01", "2026-03-02", 900)]  // impossible offset
    public async Task Invalid_ranges_return_400_with_field_errors(string from, string to, int offset)
    {
        var admin = await _arrange.AdminAsync();
        var response = await admin.GetAsync(Url(null, from, to, offset));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Missing_or_malformed_dates_return_400_never_500()
    {
        var admin = await _arrange.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/reports/performance")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/reports/performance?from=banana&to=2026-03-01")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_lot_returns_404()
    {
        var admin = await _arrange.AdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Url(Guid.NewGuid(), "2026-03-01", "2026-03-02"))).StatusCode);
    }

    [Theory]
    [InlineData("attendant@parkwise.local")]
    [InlineData("driver1@parkwise.local")]
    public async Task Reports_are_for_admins_only(string email)
    {
        var session = await factory.CreateRawClient().LoginAsync(email, ApiFactory.SeedPassword);
        var client = factory.CreateRawClient().WithBearer(session.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/occupancy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url(null, "2026-03-01", "2026-03-02"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateRawClient().GetAsync("/api/v1/reports/occupancy")).StatusCode);
    }

    [Fact]
    public async Task Occupancy_counts_every_status_and_ignores_out_of_service_spots_in_the_rate()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        await admin.PutJsonAsync($"/api/v1/spots/{spots[3].Id}/service", new { inService = false });
        await _arrange.CheckedInAsync(attendant, lot.Id);

        var report = await (await admin.GetAsync("/api/v1/reports/occupancy")).ReadAsync<OccupancyReportDto>();

        var row = Assert.Single(report.Lots, l => l.LotId == lot.Id);
        Assert.Equal((4, 2, 1, 0, 1), (row.Total, row.Available, row.Occupied, row.Reserved, row.OutOfService));
        Assert.Equal(0.3333, row.OccupancyRate);   // 1 occupied of 3 usable spots
        Assert.Equal(DateTimeKind.Utc, report.GeneratedAtUtc.Kind);
    }

    [Fact]
    public async Task Overall_occupancy_covers_open_lots_only_and_includes_the_seeded_lots()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);

        var before = await (await admin.GetAsync("/api/v1/reports/occupancy")).ReadAsync<OccupancyReportDto>();
        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });
        var after = await (await admin.GetAsync("/api/v1/reports/occupancy")).ReadAsync<OccupancyReportDto>();

        Assert.Equal(before.Overall.Total - 1, after.Overall.Total);      // the closed lot's single spot dropped out
        Assert.Contains(after.Lots, l => l.LotName == "Downtown Garage");
        Assert.Contains(after.Lots, l => l.LotId == lot.Id && !l.IsActive); // still listed, marked closed
    }
}
