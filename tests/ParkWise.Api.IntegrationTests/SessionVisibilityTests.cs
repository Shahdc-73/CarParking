using System.Net;
using ParkWise.Application.Common;
using ParkWise.Application.Sessions;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SessionVisibilityTests(ApiFactory factory)
{
    private readonly Arrange _arrange = new(factory);

    [Fact]
    public async Task Drivers_only_see_their_own_sessions_receipts_and_fee_previews()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (alice, _) = await _arrange.NewDriverAsync();
        var (bob, _) = await _arrange.NewDriverAsync();
        var aliceCar = await _arrange.VehicleAsync(alice);
        var bobCar = await _arrange.VehicleAsync(bob);
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var aliceSession = await _arrange.CheckedInAsync(attendant, lot.Id, aliceCar.PlateNumber, type: null);
        var bobSession = await _arrange.CheckedInAsync(attendant, lot.Id, bobCar.PlateNumber, type: null);

        var aliceList = await (await alice.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<SessionDto>>();
        Assert.Equal([aliceSession.Id], aliceList.Items.Select(s => s.Id));

        // someone else's session is "not found", not "forbidden"
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/sessions/{bobSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/v1/sessions/{bobSession.Id}/fee-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/v1/sessions/{aliceSession.Id}/fee-preview")).StatusCode);

        var receipt = await (await Arrange.CheckOutAsync(attendant, aliceSession.Id)).ReadAsync<ReceiptDto>();
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/v1/receipts/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/receipts/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attendant.GetAsync($"/api/v1/receipts/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attendant.GetAsync($"/api/v1/receipts/{Guid.NewGuid()}")).StatusCode);

        var history = await (await alice.GetAsync("/api/v1/sessions?status=Completed")).ReadAsync<PagedResult<SessionDto>>();
        Assert.Equal(receipt.Number, Assert.Single(history.Items).ReceiptNumber);
    }

    [Fact]
    public async Task Walk_in_sessions_without_an_owner_are_invisible_to_drivers_but_visible_to_staff()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (driver, _) = await _arrange.NewDriverAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var walkIn = await _arrange.CheckedInAsync(attendant, lot.Id);

        Assert.Empty((await (await driver.GetAsync("/api/v1/sessions")).ReadAsync<PagedResult<SessionDto>>()).Items);
        Assert.Equal(HttpStatusCode.OK, (await attendant.GetAsync($"/api/v1/sessions/{walkIn.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sessions_can_be_filtered_sorted_and_paged()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard, SpotType.Standard);
        var tag = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var first = await _arrange.CheckedInAsync(attendant, lot.Id, $"{tag}AAA");
        var second = await _arrange.CheckedInAsync(attendant, lot.Id, $"{tag}BBB");
        await _arrange.CheckedInAsync(attendant, lot.Id, $"{tag}CCC");
        await Arrange.CheckOutAsync(attendant, first.Id);

        async Task<PagedResult<SessionDto>> Query(string qs) => await (await attendant.GetAsync($"/api/v1/sessions?lotId={lot.Id}{qs}")).ReadAsync<PagedResult<SessionDto>>();

        Assert.Equal(3, (await Query("")).TotalCount);
        Assert.Equal([first.Id], (await Query("&status=Completed")).Items.Select(s => s.Id));
        Assert.Equal(2, (await Query("&status=Active")).TotalCount);
        Assert.Equal([second.Id], (await Query($"&plate={tag}bb")).Items.Select(s => s.Id));
        Assert.Equal([second.Id], (await Query($"&ticketCode={second.TicketCode.ToLowerInvariant()}")).Items.Select(s => s.Id));
        Assert.Equal([$"{tag}AAA", $"{tag}BBB", $"{tag}CCC"], (await Query("&sort=plate")).Items.Select(s => s.PlateNumber));
        Assert.Equal(first.Id, (await Query("&sort=checkin")).Items[0].Id);
        Assert.Equal(first.Id, (await Query("")).Items[^1].Id); // default: newest first

        var page = await Query("&pageSize=2&page=2&sort=plate");
        Assert.Equal((3, 1), (page.TotalCount, page.Items.Count));

        var future = await Query($"&fromUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"))}");
        Assert.Equal(0, future.TotalCount);
    }

    [Fact]
    public async Task Session_ids_that_do_not_exist_return_404()
    {
        var attendant = await _arrange.AttendantAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await attendant.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attendant.GetAsync($"/api/v1/sessions/{Guid.NewGuid()}/fee-preview")).StatusCode);
    }
}
