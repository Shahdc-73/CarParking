using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace ParkWise.Api.IntegrationTests;

/// <summary>
/// Boots the REAL API in memory against a REAL SQL Server running in Docker.
/// One container is shared by every test (see ApiCollection) because starting SQL Server is slow.
/// Tests isolate themselves with unique data (random emails), not by resetting the database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SeedPassword = "TestPassw0rd1";

    /// <summary>The API's clock. Tests move it forward to simulate long stays (see <see cref="TestClock.TravelBy"/>).</summary>
    public TestClock Clock { get; } = new();

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _sql.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _sql.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true"); // runs the real migrations, then seeds
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("Seed:Password", SeedPassword);

        // The timer-driven job is OFF in tests: the shared clock jumps forward in other tests, which would make a live job
        // expire unrelated reservations. Tests run the very same maintenance service on demand instead (Arrange.RunMaintenanceAsync).
        builder.UseSetting("Reservations:ExpiryJob:Enabled", "false");

        // Hundreds of tests log in from the same fake IP; the rate-limit tests lower this on their own host.
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>
/// Real time plus an adjustable offset. Time keeps flowing (so audit ordering stays sensible), but a test can jump
/// ahead hours or days. JWT validation still uses the real clock, so log in BEFORE travelling.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Moves the clock forward until the returned handle is disposed.</summary>
    public IDisposable TravelBy(TimeSpan span)
    {
        Interlocked.Exchange(ref _offsetTicks, span.Ticks);
        return new Reset(this);
    }

    private sealed class Reset(TestClock clock) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref clock._offsetTicks, 0);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
