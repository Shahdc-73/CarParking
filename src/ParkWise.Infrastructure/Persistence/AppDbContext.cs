using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ParkWise.Application.Common;
using ParkWise.Domain.Entities;
using ParkWise.Infrastructure.Identity;

namespace ParkWise.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IAppDbContext
{
    public DbSet<ParkingLot> Lots => Set<ParkingLot>();
    public DbSet<Level> Levels => Set<Level>();
    public DbSet<Spot> Spots => Set<Spot>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ParkingSession> Sessions => Set<ParkingSession>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<LostTicketFee> LostTicketFees => Set<LostTicketFee>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity's own tables
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are assigned in code (Guid v7), never by the database. Without this EF assumes any entity that already
        // has a key must exist, so a new child added to a tracked parent would be sent as an UPDATE instead of an INSERT.
        foreach (var entity in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(Domain.Common.Entity).IsAssignableFrom(e.ClrType) || e.ClrType == typeof(RefreshToken)))
            modelBuilder.Entity(entity.ClrType).Property(nameof(Domain.Common.Entity.Id)).ValueGeneratedNever();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server datetime2 comes back with Kind=Unspecified. Stamp it as UTC on read so the
        // rest of the app (and JSON serialization with a trailing 'Z') never mistakes it for local time.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }
}

internal sealed class UtcDateTimeConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

internal sealed class NullableUtcDateTimeConverter()
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
