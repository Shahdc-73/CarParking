using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ParkWise.Application.Auth;
using ParkWise.Application.Parking;
using ParkWise.Application.Reservations;
using ParkWise.Application.Sessions;
using ParkWise.Application.Vehicles;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Infrastructure.Persistence;
using static ParkWise.Api.IntegrationTests.TestHelpers;

namespace ParkWise.Api.IntegrationTests;

/// <summary>Builds the data each test needs through the public API (and, for history, the DbContext).</summary>
public sealed class Arrange(ApiFactory factory)
{
    public async Task<HttpClient> AdminAsync() => await LoginAsync("admin@parkwise.local");
    public async Task<HttpClient> AttendantAsync() => await LoginAsync("attendant@parkwise.local");

    /// <summary>A brand-new driver, so tests never share vehicles.</summary>
    public async Task<(HttpClient Client, Guid UserId)> NewDriverAsync()
    {
        var raw = factory.CreateRawClient();
        var session = await raw.RegisterAsync();
        return (raw.WithBearer(session.AccessToken), session.Body.User.Id);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var raw = factory.CreateRawClient();
        var session = await raw.LoginAsync(email, ApiFactory.SeedPassword);
        return raw.WithBearer(session.AccessToken);
    }

    private static async Task EnsureCreatedAsync(HttpResponseMessage response)
        => Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Arrange failed: {(int)response.StatusCode} {response.RequestMessage?.RequestUri?.PathAndQuery} {await response.Content.ReadAsStringAsync()}");

    public static string Unique(string prefix = "T") => $"{prefix}-{Guid.NewGuid():N}"[..20];

    public async Task<LotDto> LotAsync(HttpClient admin, bool overflow = false)
    {
        var response = await admin.PostJsonAsync("/api/v1/lots", new SaveLotRequest(Unique("Lot"), "1 Test Street", overflow));
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<LotDto>();
    }

    public async Task<LevelDto> LevelAsync(HttpClient admin, Guid lotId, int number = 1)
    {
        var response = await admin.PostJsonAsync($"/api/v1/lots/{lotId}/levels", new CreateLevelRequest(number, $"Level {number}"));
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<LevelDto>();
    }

    public async Task<SpotDto> SpotAsync(HttpClient admin, Guid levelId, string code, SpotType type = SpotType.Standard)
    {
        var response = await admin.PostJsonAsync($"/api/v1/levels/{levelId}/spots", new CreateSpotRequest(code, type));
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<SpotDto>();
    }

    /// <summary>A lot with one level and one spot: the smallest useful fixture.</summary>
    public async Task<(LotDto Lot, LevelDto Level, SpotDto Spot)> LotWithSpotAsync(HttpClient admin, SpotType type = SpotType.Standard)
    {
        var lot = await LotAsync(admin);
        var level = await LevelAsync(admin, lot.Id);
        var spot = await SpotAsync(admin, level.Id, "A-01", type);
        return (lot, level, spot);
    }

    public async Task<VehicleDto> VehicleAsync(HttpClient client, string? plate = null, VehicleType type = VehicleType.Car)
    {
        var response = await client.PostJsonAsync("/api/v1/vehicles", new CreateVehicleRequest(plate ?? RandomPlate(), type, "Red"));
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<VehicleDto>();
    }

    public static string RandomPlate() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // ---- gate operations ----
    public Task<HttpResponseMessage> CheckInAsync(HttpClient attendant, Guid lotId, string? plate = null, VehicleType? type = VehicleType.Car)
        => attendant.PostJsonAsync("/api/v1/sessions/check-in", new CheckInRequest(lotId, plate ?? RandomPlate(), type));

    public async Task<SessionDto> CheckedInAsync(HttpClient attendant, Guid lotId, string? plate = null, VehicleType? type = VehicleType.Car)
    {
        var response = await CheckInAsync(attendant, lotId, plate, type);
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<SessionDto>();
    }

    public static Task<HttpResponseMessage> CheckOutAsync(HttpClient attendant, Guid sessionId, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/check-out");
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return attendant.SendAsync(request);
    }

    public static bool Replayed(HttpResponseMessage response) => response.Headers.GetValues("Idempotent-Replayed").Single() == "true";

    /// <summary>A lot with one level and the given spot types ("A-01", "A-02"... in order).</summary>
    public async Task<(LotDto Lot, LevelDto Level, List<SpotDto> Spots)> LotWithSpotsAsync(HttpClient admin, params SpotType[] types)
    {
        var lot = await LotAsync(admin);
        var level = await LevelAsync(admin, lot.Id);
        var spots = new List<SpotDto>();
        for (var i = 0; i < types.Length; i++)
            spots.Add(await SpotAsync(admin, level.Id, $"A-{i + 1:00}", types[i]));
        return (lot, level, spots);
    }

    // ---- reservations ----
    public static Task<HttpResponseMessage> ReserveAsync(HttpClient client, Guid lotId, Guid vehicleId, SpotType type, DateTime start, TimeSpan length)
        => client.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(lotId, vehicleId, type, start, start + length));

    public static async Task<ReservationDto> ReservedAsync(HttpClient client, Guid lotId, Guid vehicleId, DateTime start, TimeSpan? length = null, SpotType type = SpotType.Standard)
    {
        var response = await ReserveAsync(client, lotId, vehicleId, type, start, length ?? TimeSpan.FromHours(2));
        await EnsureCreatedAsync(response);
        return await response.ReadAsync<ReservationDto>();
    }

    public static DateTime In(TimeSpan span) => DateTime.UtcNow + span;

    /// <summary>Runs the background job's work once, right now, against the (possibly time-travelled) clock.</summary>
    public async Task<MaintenanceResult> RunMaintenanceAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IReservationMaintenance>().RunOnceAsync(CancellationToken.None);
    }

    public static async Task<SpotStatus> SpotStatusAsync(HttpClient client, Guid spotId)
        => (await (await client.GetAsync($"/api/v1/spots/{spotId}")).ReadAsync<SpotDto>()).Status;

    // ---- direct database access, only for states that need features built in later phases ----
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Starts an ACTIVE parking session (spot becomes Occupied). Check-in itself arrives in Phase 5.</summary>
    public Task StartSessionAsync(Guid spotId, Guid vehicleId) => WithDbAsync(async db =>
    {
        var spot = await db.Spots.FindAsync(spotId);
        spot!.Occupy();
        db.Sessions.Add(new ParkingSession(vehicleId, spotId, $"TK-{Guid.NewGuid():N}"[..20], DateTime.UtcNow, Guid.NewGuid()));
        await db.SaveChangesAsync();
    });

    /// <summary>Closes the active session and frees the spot: history remains.</summary>
    public Task EndSessionAsync(Guid spotId) => WithDbAsync(async db =>
    {
        var spot = await db.Spots.FindAsync(spotId);
        var session = db.Sessions.Single(s => s.SpotId == spotId && s.Status == SessionStatus.Active);
        session.CheckOut(DateTime.UtcNow);
        spot!.Release();
        await db.SaveChangesAsync();
    });

    public Task AddPendingReservationAsync(Guid spotId, Guid vehicleId) => WithDbAsync(async db =>
    {
        var now = DateTime.UtcNow;
        db.Reservations.Add(new Reservation(Guid.NewGuid(), vehicleId, spotId, now.AddHours(1), now.AddHours(2), now));
        await db.SaveChangesAsync();
    });
}
