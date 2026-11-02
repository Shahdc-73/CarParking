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
