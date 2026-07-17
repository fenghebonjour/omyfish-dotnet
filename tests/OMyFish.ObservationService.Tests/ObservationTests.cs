using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Domain.Events;
using OMyFish.ObservationService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.ObservationService.Tests;

public class ObservationTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_SetsFieldsAndLocation()
    {
        var obs = Observation.Create(UserId, "Walleye", "Sander vitreus", 0.91,
            "img/key.jpg", GpsCoordinates.Create(45.5, -73.5), null, "caught at dawn");

        Assert.Equal(UserId, obs.UserId);
        Assert.Equal("Walleye", obs.SpeciesName);
        Assert.Equal(45.5, obs.Location?.Latitude);
        Assert.Equal(-73.5, obs.Location?.Longitude);
        Assert.Equal("caught at dawn", obs.Notes);
    }

    [Fact]
    public void Create_WithoutLocation_LocationIsNull()
    {
        var obs = Observation.Create(UserId, "Walleye", null, 0.91,
            "img/key.jpg", null, null, null);

        Assert.Null(obs.Location);
    }

    [Fact]
    public void Create_RegistersObservationCreatedEvent()
    {
        var obs = Observation.Create(UserId, "Walleye", null, 0.91,
            "img/key.jpg", GpsCoordinates.Create(45.5, -73.5), null, null);

        var evt = Assert.IsType<ObservationCreatedEvent>(Assert.Single(obs.DomainEvents));
        Assert.Equal(obs.Id, evt.ObservationId);
        Assert.Equal("Walleye", evt.SpeciesName);
        Assert.Equal(45.5, evt.Latitude);
    }

    [Fact]
    public void PullDomainEvents_ReturnsOnceThenClears()
    {
        var obs = Observation.Create(UserId, "Walleye", null, 0.91,
            "img/key.jpg", null, null, null);

        Assert.Single(obs.PullDomainEvents());
        Assert.Empty(obs.PullDomainEvents());
        Assert.Empty(obs.DomainEvents);
    }

    [Fact]
    public void Create_WithoutExif_ObservedAtDefaultsToNow()
    {
        var before = DateTime.UtcNow;
        var obs = Observation.Create(UserId, "Walleye", null, 0.91,
            "img/key.jpg", null, null, null);

        Assert.InRange(obs.ObservedAt, before, DateTime.UtcNow);
    }
}
