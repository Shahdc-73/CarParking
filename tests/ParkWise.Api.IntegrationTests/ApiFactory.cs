using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _sql.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _sql.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true"); // runs the real migrations, then seeds
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("Seed:Password", SeedPassword);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
