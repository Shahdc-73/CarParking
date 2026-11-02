using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkWise.Domain.Entities;

namespace ParkWise.Infrastructure.Persistence.Configurations;

internal sealed class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public const string ActiveSpotIndex = "UX_Sessions_SpotId_Active";
    public const string ActiveVehicleIndex = "UX_Sessions_VehicleId_Active";

    public void Configure(EntityTypeBuilder<ParkingSession> b)
    {
        b.Property(x => x.TicketCode).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TicketCode).IsUnique();

        // The core concurrency guarantee: the database itself refuses a second ACTIVE session
        // for the same spot or the same vehicle, even if two requests race past the app-level check.
        b.HasIndex(x => x.SpotId).IsUnique().HasDatabaseName(ActiveSpotIndex).HasFilter("[Status] = 'Active'");
        b.HasIndex(x => x.VehicleId).IsUnique().HasDatabaseName(ActiveVehicleIndex).HasFilter("[Status] = 'Active'");

        // Reports query by time range.
        b.HasIndex(x => x.CheckInAtUtc);
        b.HasIndex(x => x.CheckOutAtUtc);

        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Spot>().WithMany().HasForeignKey(x => x.SpotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Payment).WithOne().HasForeignKey<Payment>(p => p.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Receipt).WithOne().HasForeignKey<Receipt>(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.HoldExpiresAtUtc);
        b.Ignore(x => x.IsPending);

        // Overlap detection and the expiry job both scan by spot + time window.
        b.HasIndex(x => new { x.SpotId, x.StartUtc, x.EndUtc });
        b.HasIndex(x => new { x.Status, x.StartUtc });
        b.HasIndex(x => x.DriverUserId);

        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Spot>().WithMany().HasForeignKey(x => x.SpotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> b)
    {
        b.Property(x => x.SpotType).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.SpotType, x.EffectiveFromUtc }).IsUnique();
    }
}

internal sealed class LostTicketFeeConfiguration : IEntityTypeConfiguration<LostTicketFee>
{
    public void Configure(EntityTypeBuilder<LostTicketFee> b)
        => b.HasIndex(x => x.EffectiveFromUtc).IsUnique();
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);

        // One payment per session: this is what makes check-out idempotent at the database level.
        b.HasIndex(x => x.SessionId).IsUnique();
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.Property(x => x.Number).HasMaxLength(30);
        b.Property(x => x.BreakdownJson).HasMaxLength(4000);
        b.HasIndex(x => x.SessionId).IsUnique();
        b.HasIndex(x => x.Number).IsUnique();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.BeforeJson).HasMaxLength(4000);
        b.Property(x => x.AfterJson).HasMaxLength(4000);
        b.HasIndex(x => x.TimestampUtc);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.ActorUserId);
    }
}
