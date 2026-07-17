using OMyFish.ObservationService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ObservationService.Tests;

public class GpsCoordinatesTests
{
    [Theory]
    [InlineData(90.01, 0)]
    [InlineData(-90.01, 0)]
    [InlineData(0, 180.01)]
    [InlineData(0, -180.01)]
    public void Create_RejectsOutOfRangeCoordinates(double lat, double lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GpsCoordinates.Create(lat, lon));
    }

    [Fact]
    public void Create_AcceptsBoundaryValues()
    {
        var coords = GpsCoordinates.Create(90, -180);
        Assert.Equal(90, coords.Latitude);
        Assert.Equal(-180, coords.Longitude);
    }

    [Fact]
    public void ToWkt_UsesLongitudeLatitudeOrder()
    {
        Assert.Equal("POINT(-73.5 45.5)", GpsCoordinates.Create(45.5, -73.5).ToWkt());
    }
}
