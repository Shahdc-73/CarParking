using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkWise.Domain.Entities;

namespace ParkWise.Infrastructure.Persistence.Configurations;

internal sealed class ParkingLotConfiguration : IEntityTypeConfiguration<ParkingLot>
{
    public void Configure(EntityTypeBuilder<ParkingLot> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(300);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasMany(x => x.Levels).WithOne().HasForeignKey(l => l.LotId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LevelConfiguration : IEntityTypeConfiguration<Level>
{
    public void Configure(EntityTypeBuilder<Level> b)
    {
        b.Property(x => x.Name).HasMaxLength(50);
        b.HasIndex(x => new { x.LotId, x.Number }).IsUnique();
        b.HasMany(x => x.Spots).WithOne().HasForeignKey(s => s.LevelId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SpotConfiguration : IEntityTypeConfiguration<Spot>
{
    public void Configure(EntityTypeBuilder<Spot> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.LevelId, x.Code }).IsUnique();
        // Supports "find a free spot of type X" at check-in.
        b.HasIndex(x => new { x.Type, x.Status });
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.Property(x => x.PlateNumber).HasMaxLength(10);
        b.Property(x => x.Color).HasMaxLength(30);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.PlateNumber).IsUnique();
        b.HasIndex(x => x.OwnerUserId);
    }
}
