using NSubstitute;
using OMyFish.ObservationService.Application.Commands;
using OMyFish.ObservationService.Application.Interfaces;
using OMyFish.ObservationService.Domain.Entities;
using OMyFish.ObservationService.Domain.Events;
using OMyFish.Shared.BuildingBlocks.Messaging;
using Xunit;

namespace OMyFish.ObservationService.Tests;

public class CreateObservationCommandHandlerTests
{
    private readonly IObservationRepository _repo = Substitute.For<IObservationRepository>();
    private readonly IStorageService _storage = Substitute.For<IStorageService>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly CreateObservationCommandHandler _handler;

    public CreateObservationCommandHandlerTests()
    {
        _handler = new CreateObservationCommandHandler(_repo, _storage, _publisher);
        _storage.UploadAsync(Arg.Any<Stream>(), "fish.jpg", "image/jpeg", Arg.Any<CancellationToken>())
            .Returns("stored/fish.jpg");
    }

    private static CreateObservationCommand Command(double? lat = null, double? lon = null) =>
        new(Guid.NewGuid(), "Walleye", "Sander vitreus", 0.91,
            new MemoryStream([1, 2, 3]), "fish.jpg", "image/jpeg", lat, lon, null);

    [Fact]
    public async Task Handle_UploadsImageAndSavesObservation()
    {
        Observation? saved = null;
        await _repo.AddAsync(Arg.Do<Observation>(o => saved = o), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(Command(45.5, -73.5), CancellationToken.None);

        Assert.Equal("stored/fish.jpg", result.ImageStorageKey);
        Assert.NotNull(saved);
        Assert.Equal("Walleye", saved.SpeciesName);
        Assert.Equal("stored/fish.jpg", saved.ImageStorageKey);
        Assert.Equal(45.5, saved.Location?.Latitude);
        Assert.Equal(result.ObservationId, saved.Id);
    }

    [Fact]
    public async Task Handle_PublishesObservationCreatedEvent()
    {
        await _handler.Handle(Command(), CancellationToken.None);

        await _publisher.Received(1).PublishAsync(
            Arg.Any<ObservationCreatedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutCoordinates_SavesObservationWithNullLocation()
    {
        Observation? saved = null;
        await _repo.AddAsync(Arg.Do<Observation>(o => saved = o), Arg.Any<CancellationToken>());

        await _handler.Handle(Command(), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Null(saved.Location);
    }
}
