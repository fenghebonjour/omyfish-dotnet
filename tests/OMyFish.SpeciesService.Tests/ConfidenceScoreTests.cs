using OMyFish.SpeciesService.Domain.ValueObjects;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

public class ConfidenceScoreTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_RejectsValuesOutsideZeroToOne(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceScore.Create(value));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Create_AcceptsBoundaryValues(double value)
    {
        Assert.Equal(value, ConfidenceScore.Create(value).Value);
    }

    [Fact]
    public void IsUncertain_BelowThreshold()
    {
        Assert.True(ConfidenceScore.Create(0.29).IsUncertain);
        Assert.False(ConfidenceScore.Create(0.30).IsUncertain);
    }

    [Fact]
    public void IsHighConfidence_AtOrAboveThreshold()
    {
        Assert.False(ConfidenceScore.Create(0.84).IsHighConfidence);
        Assert.True(ConfidenceScore.Create(0.85).IsHighConfidence);
    }
}
