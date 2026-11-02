using ParkWise.Domain.Entities;
using ParkWise.Domain.Enums;
using ParkWise.Domain.Exceptions;

namespace ParkWise.Domain.Tests;

public class SpotTests
{
    private static Spot NewSpot()
    {
        var lot = new ParkingLot("Lot", "Addr");
        return lot.AddLevel(1, "L1").AddSpot("a-01", SpotType.Standard);
    }

    [Fact]
    public void New_spot_is_available_with_normalized_code()
    {
        var spot = NewSpot();
        Assert.Equal(SpotStatus.Available, spot.Status);
        Assert.Equal("A-01", spot.Code);
    }

    [Fact]
    public void Occupied_spot_cannot_be_occupied_again()
    {
        var spot = NewSpot();
        spot.Occupy();
        Assert.Throws<DomainException>(spot.Occupy);
    }

    [Fact]
    public void Reserved_spot_can_be_occupied_by_the_reservation_holder()
    {
        var spot = NewSpot();
        spot.Reserve();
        spot.Occupy();
        Assert.Equal(SpotStatus.Occupied, spot.Status);
    }

    [Fact]
    public void Occupied_spot_cannot_go_out_of_service()
    {
        var spot = NewSpot();
        spot.Occupy();
        Assert.Throws<DomainException>(spot.SetOutOfService);
    }

    [Fact]
    public void Out_of_service_spot_cannot_be_reserved_or_occupied()
    {
        var spot = NewSpot();
        spot.SetOutOfService();
        Assert.Throws<DomainException>(spot.Reserve);
        Assert.Throws<DomainException>(spot.Occupy);
    }

    [Fact]
    public void Release_returns_spot_to_available_and_requires_it_to_be_in_use()
    {
        var spot = NewSpot();
        Assert.Throws<DomainException>(spot.Release);
        spot.Occupy();
        spot.Release();
        Assert.Equal(SpotStatus.Available, spot.Status);
    }

    [Fact]
    public void Duplicate_spot_code_on_same_level_is_rejected()
    {
        var level = new ParkingLot("Lot", "Addr").AddLevel(1, "L1");
        level.AddSpot("A-01", SpotType.Standard);
        Assert.Throws<DomainException>(() => level.AddSpot("a-01", SpotType.EV));
    }
}

public class SpotMaintenanceTests
{
    private static Spot NewSpot(SpotType type = SpotType.Standard)
        => new ParkingLot("Lot", "Addr").AddLevel(1, "L1").AddSpot("A-01", type);

    [Fact]
    public void Reserved_spot_cannot_be_taken_out_of_service()
    {
        var spot = NewSpot();
        spot.Reserve();
        Assert.Throws<DomainException>(spot.SetOutOfService);
        Assert.Equal(SpotStatus.Reserved, spot.Status);
    }

    [Theory]
    [InlineData(SpotStatus.Occupied)]
    [InlineData(SpotStatus.Reserved)]
    public void Type_cannot_change_while_in_use(SpotStatus status)
    {
        var spot = NewSpot();
        if (status == SpotStatus.Occupied) spot.Occupy(); else spot.Reserve();
        Assert.Throws<DomainException>(() => spot.ChangeType(SpotType.EV));
        Assert.Equal(SpotType.Standard, spot.Type);
    }

    [Fact]
    public void Type_can_change_when_available_or_out_of_service()
    {
        var spot = NewSpot();
        spot.ChangeType(SpotType.EV);
        spot.SetOutOfService();
        spot.ChangeType(SpotType.Compact);
        Assert.Equal(SpotType.Compact, spot.Type);
    }

    [Fact]
    public void Rename_normalizes_the_code()
    {
        var spot = NewSpot();
        spot.Rename("  b-07 ");
        Assert.Equal("B-07", spot.Code);
    }

    [Fact]
    public void Level_rename_rejects_blank_names()
    {
        var level = new ParkingLot("Lot", "Addr").AddLevel(1, "L1");
        level.Rename("Roof");
        Assert.Equal("Roof", level.Name);
        Assert.ThrowsAny<ArgumentException>(() => level.Rename(" "));
    }

    [Fact]
    public void Duplicate_level_numbers_in_a_lot_are_rejected()
    {
        var lot = new ParkingLot("Lot", "Addr");
        lot.AddLevel(1, "L1");
        Assert.Throws<DomainException>(() => lot.AddLevel(1, "Again"));
    }
}
