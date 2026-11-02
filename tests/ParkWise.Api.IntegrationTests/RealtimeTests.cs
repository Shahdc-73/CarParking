using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ParkWise.Api.Hubs;
using ParkWise.Application.Reservations;
using ParkWise.Domain.Enums;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RealtimeTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly Arrange _arrange = new(factory);
    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _connections) await c.DisposeAsync();
    }

    /// <summary>A real SignalR client speaking WebSockets to the in-memory server, with the token in the query string like a browser.</summary>
    private HubConnection Build(string? accessToken)
    {
        _ = factory.CreateClient(); // makes sure the test server exists
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/availability"), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = false;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                // A browser cannot set headers on a WebSocket, so it puts the token in the query string. Do exactly that.
                o.WebSocketFactory = (context, ct) =>
                {
                    var uri = accessToken is null ? context.Uri : new Uri($"{context.Uri}{(context.Uri.Query.Length > 0 ? "&" : "?")}access_token={Uri.EscapeDataString(accessToken)}");
                    return new ValueTask<System.Net.WebSockets.WebSocket>(factory.Server.CreateWebSocketClient().ConnectAsync(uri, ct));
                };
                o.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    private sealed class Watcher(HubConnection connection)
    {
        private readonly Channel<Guid> _events = Channel.CreateUnbounded<Guid>();

        public HubConnection Connection => connection;

        public void Listen() => connection.On<Guid>("LotChanged", id => _events.Writer.TryWrite(id));

        public async Task<Guid?> NextAsync(int milliseconds = 3000)
        {
            using var cts = new CancellationTokenSource(milliseconds);
            try { return await _events.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
        }

        /// <summary>Waits briefly and reports whether anything at all arrived.</summary>
        public async Task<bool> AnythingAsync(int milliseconds = 600) => await NextAsync(milliseconds) is not null;
    }

    private async Task<string> TokenAsync(string email)
        => (await factory.CreateRawClient().LoginAsync(email, ApiFactory.SeedPassword)).AccessToken;

    private async Task<Watcher> WatchAsync(string email, Guid? lotId = null, bool all = false)
    {
        var connection = Build(await TokenAsync(email));
        var watcher = new Watcher(connection);
        watcher.Listen();
        await connection.StartAsync();
        if (lotId is { } id) await connection.InvokeAsync("JoinLot", id);
        if (all) await connection.InvokeAsync("JoinAllLots");
        return watcher;
    }

    // ---- authentication ----

    [Fact]
    public async Task An_anonymous_client_cannot_connect()
    {
        var connection = Build(accessToken: null);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.ToString());
    }

    [Fact]
    public async Task A_forged_or_tampered_token_cannot_connect()
    {
        var real = await TokenAsync("driver1@parkwise.local");
        foreach (var bad in new[] { "not-a-token", real[..^3] + "abc" })
            await Assert.ThrowsAnyAsync<Exception>(() => Build(bad).StartAsync());
    }

    [Fact]
    public async Task Any_signed_in_role_may_listen_because_the_message_carries_no_data()
    {
        foreach (var email in new[] { "driver1@parkwise.local", "attendant@parkwise.local", "admin@parkwise.local" })
        {
            var connection = Build(await TokenAsync(email));
            await connection.StartAsync();
            Assert.Equal(HubConnectionState.Connected, connection.State);
        }
    }

    [Fact]
    public async Task The_token_is_only_accepted_in_the_query_string_for_hub_paths()
    {
        var token = await TokenAsync("attendant@parkwise.local");

        var other = await factory.CreateRawClient().GetAsync($"/api/v1/lots?access_token={token}");

        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode); // a normal API call must NOT honour ?access_token=
    }

    // ---- who hears what ----

    [Fact]
    public async Task Watchers_of_a_lot_hear_about_a_check_in_there_and_others_do_not()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lotA, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var (lotB, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var inA = await WatchAsync("driver1@parkwise.local", lotId: lotA.Id);
        var inB = await WatchAsync("driver1@parkwise.local", lotId: lotB.Id);

        await _arrange.CheckedInAsync(attendant, lotA.Id);

        Assert.Equal(lotA.Id, await inA.NextAsync());
        Assert.False(await inB.AnythingAsync());
    }

    [Fact]
    public async Task An_all_lots_watcher_hears_about_every_lot()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lotA, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var (lotB, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var all = await WatchAsync("admin@parkwise.local", all: true);

        await _arrange.CheckedInAsync(attendant, lotA.Id);
        await _arrange.CheckedInAsync(attendant, lotB.Id);

        var heard = new List<Guid>();
        while (await all.NextAsync(1500) is { } id) heard.Add(id);
        Assert.Contains(lotA.Id, heard);
        Assert.Contains(lotB.Id, heard);
    }

    [Fact]
    public async Task After_leaving_a_lot_nothing_more_arrives()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var watcher = await WatchAsync("attendant@parkwise.local", lotId: lot.Id);
        await _arrange.CheckedInAsync(attendant, lot.Id);
        Assert.Equal(lot.Id, await watcher.NextAsync());

        await watcher.Connection.InvokeAsync("LeaveLot", lot.Id);
        await _arrange.CheckedInAsync(attendant, lot.Id);

        Assert.False(await watcher.AnythingAsync());
    }

    [Fact]
    public async Task The_message_is_just_the_lot_id_and_never_a_plate_or_a_fee()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var connection = Build(await TokenAsync("driver1@parkwise.local"));
        var arguments = Channel.CreateUnbounded<object?[]>();
        connection.On("LotChanged", [typeof(Guid)], args =>
        {
            arguments.Writer.TryWrite(args);
            return Task.CompletedTask;
        });
        await connection.StartAsync();
        await connection.InvokeAsync("JoinLot", lot.Id);
        var plate = Arrange.RandomPlate();

        await _arrange.CheckedInAsync(attendant, lot.Id, plate);

        using var cts = new CancellationTokenSource(3000);
        var received = await arguments.Reader.ReadAsync(cts.Token);
        var onlyArgument = Assert.Single(received);
        Assert.Equal(lot.Id, Assert.IsType<Guid>(onlyArgument)); // one argument, and it is the lot id
        Assert.DoesNotContain(plate, string.Join("|", received));
    }

    // ---- every kind of change notifies ----

    [Fact]
    public async Task Check_out_and_lost_ticket_notify()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard, SpotType.Standard);
        var a = await _arrange.CheckedInAsync(attendant, lot.Id);
        var b = await _arrange.CheckedInAsync(attendant, lot.Id);
        var watcher = await WatchAsync("admin@parkwise.local", lotId: lot.Id);

        await Arrange.CheckOutAsync(attendant, a.Id);
        Assert.Equal(lot.Id, await watcher.NextAsync());

        await attendant.PostJsonAsync("/api/v1/sessions/lost-ticket", new ParkWise.Application.Sessions.LostTicketRequest(b.PlateNumber));
        Assert.Equal(lot.Id, await watcher.NextAsync());
    }

    [Fact]
    public async Task Reservations_notify_when_made_and_when_cancelled_during_the_hold()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var car = await _arrange.VehicleAsync(attendant);
        var watcher = await WatchAsync("admin@parkwise.local", lotId: lot.Id);

        var reservation = await Arrange.ReservedAsync(attendant, lot.Id, car.Id, Arrange.In(TimeSpan.FromHours(1)));
        Assert.Equal(lot.Id, await watcher.NextAsync());

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2));
        await _arrange.RunMaintenanceAsync();                         // the background job starts the hold...
        Assert.Equal(lot.Id, await watcher.NextAsync());              // ...and the board hears about it

        await attendant.DeleteAsync($"/api/v1/reservations/{reservation.Id}");
        Assert.Equal(lot.Id, await watcher.NextAsync());              // cancelling releases the held spot
    }

    [Fact]
    public async Task The_background_job_notifies_when_a_held_spot_is_released_after_a_missed_reservation()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, Arrange.In(TimeSpan.FromHours(1)));
        var watcher = await WatchAsync("admin@parkwise.local", lotId: lot.Id);

        using (factory.Clock.TravelBy(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2)))
        {
            await _arrange.RunMaintenanceAsync();                    // the hold starts: the spot turns Reserved
            Assert.Equal(lot.Id, await watcher.NextAsync());
        }

        using (factory.Clock.TravelBy(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(20)))
        {
            await _arrange.RunMaintenanceAsync();                    // nobody came: the hold ends and the spot is free again
            Assert.Equal(lot.Id, await watcher.NextAsync());
        }
    }

    [Fact]
    public async Task A_missed_reservation_whose_hold_never_started_changes_nothing_on_the_board_so_it_stays_quiet()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        await Arrange.ReservedAsync(attendant, lot.Id, (await _arrange.VehicleAsync(attendant)).Id, Arrange.In(TimeSpan.FromHours(1)));
        var watcher = await WatchAsync("admin@parkwise.local", lotId: lot.Id);

        using var _ = factory.Clock.TravelBy(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(20)); // the job was down; it expires without ever holding
        await _arrange.RunMaintenanceAsync();

        Assert.False(await watcher.AnythingAsync());
    }

    [Fact]
    public async Task Admin_changes_to_spots_and_lots_notify()
    {
        var admin = await _arrange.AdminAsync();
        var (lot, level, spots) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        var watcher = await WatchAsync("driver1@parkwise.local", lotId: lot.Id);

        await admin.PutJsonAsync($"/api/v1/spots/{spots[0].Id}/service", new { inService = false });
        Assert.Equal(lot.Id, await watcher.NextAsync());

        await admin.PostJsonAsync($"/api/v1/levels/{level.Id}/spots", new ParkWise.Application.Parking.CreateSpotRequest("A-99", SpotType.EV));
        Assert.Equal(lot.Id, await watcher.NextAsync());

        await admin.PutJsonAsync($"/api/v1/lots/{lot.Id}/active", new { isActive = false });
        Assert.Equal(lot.Id, await watcher.NextAsync());
    }

    [Fact]
    public async Task A_refused_operation_sends_no_notification()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);
        await _arrange.CheckedInAsync(attendant, lot.Id);
        var watcher = await WatchAsync("admin@parkwise.local", lotId: lot.Id);

        var full = await _arrange.CheckInAsync(attendant, lot.Id);         // lot is full: 409

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.False(await watcher.AnythingAsync());
    }

    // ---- resilience ----

    [Fact]
    public async Task A_broken_real_time_channel_never_fails_the_operation_that_triggered_it()
    {
        var clients = Substitute.For<IHubClients>();
        clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(_ => throw new InvalidOperationException("hub is down"));
        var hub = Substitute.For<IHubContext<AvailabilityHub>>();
        hub.Clients.Returns(clients);
        var notifier = new HubAvailabilityNotifier(hub, NullLogger<HubAvailabilityNotifier>.Instance);

        await notifier.LotChangedAsync(Guid.NewGuid()); // must not throw
    }

    [Fact]
    public async Task Check_in_still_succeeds_when_nobody_is_listening()
    {
        var admin = await _arrange.AdminAsync();
        var attendant = await _arrange.AttendantAsync();
        var (lot, _, _) = await _arrange.LotWithSpotsAsync(admin, SpotType.Standard);

        Assert.Equal(HttpStatusCode.Created, (await _arrange.CheckInAsync(attendant, lot.Id)).StatusCode);
    }
}
