using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParkWise.Application.Auth;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure.Identity;

/// <summary>Seeds roles always; seeds demo users only when Seed:Password is configured, so production never gets default accounts.</summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(IdentitySeeder));

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new ApplicationRole(role));

        var password = config["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            log.LogInformation("Seed:Password not set; skipping demo users.");
            return;
        }

        await EnsureUserAsync(users, "admin@parkwise.local", "Alex Admin", Roles.Admin, password);
        await EnsureUserAsync(users, "attendant@parkwise.local", "Sam Attendant", Roles.Attendant, password);
        var d1 = await EnsureUserAsync(users, "driver1@parkwise.local", "Dana Driver", Roles.Driver, password);
        var d2 = await EnsureUserAsync(users, "driver2@parkwise.local", "Drew Driver", Roles.Driver, password);

        if (!await db.Vehicles.AnyAsync(ct))
        {
            db.Vehicles.Add(new Vehicle("DRV-001", VehicleType.Car, "Blue", d1.Id));
            db.Vehicles.Add(new Vehicle("DRV-002", VehicleType.ElectricCar, "White", d2.Id));
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users, string email, string fullName, string role, string password)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null) return existing;

        var user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seeding {email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await users.AddToRoleAsync(user, role);
        return user;
    }
}
