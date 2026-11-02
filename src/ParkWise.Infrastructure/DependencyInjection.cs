using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        // AddDbContext registers AppDbContext as SCOPED: one instance per HTTP request, so a unit of work
        // (change tracker + transaction) never leaks across requests. Singleton would be a thread-safety bug.
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
