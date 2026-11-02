using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ParkWise.Domain.Entities;
using ParkWise.Infrastructure.Persistence;

namespace ParkWise.Infrastructure.Tests;

/// <summary>
/// These inspect the EF model without a database connection. They guard the concurrency-critical
/// schema rules so nobody can silently remove them. (Real SQL behaviour is covered by Testcontainers
/// integration tests from Phase 3 onward.)
/// </summary>
public class ModelTests
{
    private static AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

    private static IModel DesignModel(AppDbContext db) => db.GetService<IDesignTimeModel>().Model;

    [Fact]
    public void Only_one_active_session_per_spot_and_per_vehicle_is_enforced_by_filtered_unique_indexes()
    {
        using var db = NewContext();
        var indexes = DesignModel(db).FindEntityType(typeof(ParkingSession))!.GetIndexes().ToList();

        foreach (var column in new[] { nameof(ParkingSession.SpotId), nameof(ParkingSession.VehicleId) })
        {
            var index = Assert.Single(indexes, i => i.Properties.Count == 1 && i.Properties[0].Name == column);
            Assert.True(index.IsUnique);
            Assert.Equal("[Status] = 'Active'", index.GetFilter());
        }
    }

    [Fact]
    public void Plate_number_is_unique()
    {
        using var db = NewContext();
        var index = Assert.Single(DesignModel(db).FindEntityType(typeof(Vehicle))!.GetIndexes(),
            i => i.Properties.Any(p => p.Name == nameof(Vehicle.PlateNumber)));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void One_payment_and_one_receipt_per_session()
    {
        using var db = NewContext();
        foreach (var type in new[] { typeof(Payment), typeof(Receipt) })
        {
            var index = Assert.Single(DesignModel(db).FindEntityType(type)!.GetIndexes(),
                i => i.Properties.Count == 1 && i.Properties[0].Name == "SessionId");
            Assert.True(index.IsUnique);
        }
    }

    [Fact]
    public void Spot_reservation_and_vehicle_have_rowversion_concurrency_tokens()
    {
        using var db = NewContext();
        foreach (var type in new[] { typeof(Spot), typeof(Reservation), typeof(Vehicle) })
        {
            var property = DesignModel(db).FindEntityType(type)!.FindProperty("RowVersion")!;
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    [Fact]
    public void All_money_columns_are_decimal_18_2()
    {
        using var db = NewContext();
        var decimals = DesignModel(db).GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(decimal))
            .ToList();

        Assert.NotEmpty(decimals);
        Assert.All(decimals, p =>
        {
            Assert.Equal(18, p.GetPrecision());
            Assert.Equal(2, p.GetScale());
        });
    }

    [Fact]
    public void Model_matches_the_latest_migration_snapshot()
    {
        using var db = NewContext();
        Assert.False(db.Database.HasPendingModelChanges(),
            "The model changed but no migration was added. Run: dotnet tool run dotnet-ef migrations add <Name> -p src/ParkWise.Infrastructure -s src/ParkWise.Api -o Persistence/Migrations");
    }
}
