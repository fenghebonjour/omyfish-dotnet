using NSubstitute;
using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Application.Queries;
using Xunit;

namespace OMyFish.ObservationService.Tests;

public class GetNearbyObservationsQueryHandlerTests
{
    private readonly IObservationRepository _repo = Substitute.For<IObservationRepository>();
    private readonly GetNearbyObservationsQueryHandler _handler;

    public GetNearbyObservationsQueryHandlerTests()
    {
        _handler = new GetNearbyObservationsQueryHandler(_repo);
    }

    [Fact]
    public async Task Handle_DelegatesToRepositoryWithQueryParameters()
    {
        var expected = new List<NearbyObservationDto>
        {
            new(Guid.NewGuid(), "Walleye", 0.91, 45.5, -73.5, DateTime.UtcNow, 2.3)
        };
        _repo.GetNearbyAsync(45.5, -73.5, 10, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _handler.Handle(
            new GetNearbyObservationsQuery(45.5, -73.5, 10), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
