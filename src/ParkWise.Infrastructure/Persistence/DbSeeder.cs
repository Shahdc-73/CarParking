using Microsoft.EntityFrameworkCore;
using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;

namespace ParkWise.Infrastructure.Persistence;

/// <summary>Idempotent reference data: 2 lots / 5 levels / 60 spots, pricing, lost-ticket fee. Users are seeded in Phase 3.</summary>
public static class DbSeeder
{
    private static readonly DateTime PricingStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.Lots.AnyAsync(ct))
        {
            db.Lots.Add(BuildLot("Downtown Garage", "12 Main Street", overflow: false, 'D',
            [
                (1, "Ground", [(SpotType.Disabled, 2), (SpotType.Motorcycle, 2), (SpotType.Compact, 2), (SpotType.Standard, 6)]),
                (2, "Level 2", [(SpotType.EV, 2), (SpotType.Compact, 2), (SpotType.Standard, 8)]),
                (3, "Level 3", [(SpotType.EV, 1), (SpotType.Standard, 7)]),
            ]));

            db.Lots.Add(BuildLot("Airport Parking", "Terminal Road 1", overflow: true, 'A',
            [
                (1, "Short Stay", [(SpotType.Disabled, 2), (SpotType.EV, 2), (SpotType.Motorcycle, 2), (SpotType.Standard, 8)]),
                (2, "Long Stay", [(SpotType.Compact, 4), (SpotType.Standard, 10)]),
            ]));
        }

        if (!await db.PricingRules.AnyAsync(ct))
        {
            db.PricingRules.AddRange(
                new PricingRule(SpotType.Standard, 3.00m, 20.00m, 15, PricingStart),
                new PricingRule(SpotType.Compact, 2.50m, 18.00m, 15, PricingStart),
                new PricingRule(SpotType.EV, 3.50m, 24.00m, 15, PricingStart),
                new PricingRule(SpotType.Disabled, 2.00m, 15.00m, 15, PricingStart),
                new PricingRule(SpotType.Motorcycle, 1.50m, 10.00m, 15, PricingStart));
        }

        if (!await db.LostTicketFees.AnyAsync(ct))
            db.LostTicketFees.Add(new LostTicketFee(50.00m, PricingStart));

        await db.SaveChangesAsync(ct);
    }

    private static ParkingLot BuildLot(string name, string address, bool overflow, char prefix,
        (int Number, string Name, (SpotType Type, int Count)[] Layout)[] levels)
    {
        var lot = new ParkingLot(name, address, overflow);
        foreach (var (number, levelName, layout) in levels)
        {
            var level = lot.AddLevel(number, levelName);
            var index = 1;
            foreach (var (type, count) in layout)
                for (var i = 0; i < count; i++)
                    level.AddSpot($"{prefix}{number}-{index++:00}", type);
        }
        return lot;
    }
}
