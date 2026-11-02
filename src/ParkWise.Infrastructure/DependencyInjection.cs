using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkWise.Application.Auth;
using ParkWise.Application.Common;
using ParkWise.Application.Users;
using ParkWise.Infrastructure.Identity;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // AddDbContext registers AppDbContext as SCOPED: one instance per HTTP request, so a unit of work
        // (change tracker + transaction) never leaks across requests. Singleton would be a thread-safety bug.
        // The connection string is read lazily (inside the factory) so test hosts can override configuration.
        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
            o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure());
        });

        // Same scoped instance behind both types, so services using IAppDbContext and Identity share one unit of work.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton(TimeProvider.System);

        // AddIdentityCore (not AddIdentity): we only want users/roles/password hashing, not cookie auth or UI.
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        // Fail at startup, not at the first login, if the signing key is missing or weak.
        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped: they depend on the scoped DbContext/UserManager. A singleton here would capture one request's DbContext forever.
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        // Stateless and holds no scoped dependencies, so a singleton is safe and cheaper.
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
