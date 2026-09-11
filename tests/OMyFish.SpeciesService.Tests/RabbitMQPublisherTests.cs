using MassTransit;
using NSubstitute;
using OMyFish.Shared.BuildingBlocks.Domain;
using OMyFish.SpeciesService.Domain.Events;
using OMyFish.SpeciesService.Infrastructure.Messaging;
using Xunit;
using ContractEvents = OMyFish.Shared.Contracts.Events;

namespace OMyFish.SpeciesService.Tests;

// RabbitMQPublisher had zero test coverage (BACKLOG.md item F, Testing/CI tier). It's a thin
// adapter, but the domain-to-integration-event mapping and the "unrecognized event type is a
// silent no-op" behavior are both worth locking in — a typo in the mapping or a future domain
// event added without updating this class would otherwise only surface live.
public class RabbitMQPublisherTests
{
    private sealed record OtherDomainEvent : DomainEvent
    {
        public OtherDomainEvent() : base("other.event") { }
    }

    [Fact]
    public async Task PublishAsync_FishIdentifiedEvent_PublishesTheMappedIntegrationEvent()
    {
        var bus = Substitute.For<IPublishEndpoint>();
        var publisher = new RabbitMQPublisher(bus);
        var predictionId = Guid.NewGuid();
        var observationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var domainEvent = new FishIdentifiedEvent(
            predictionId, observationId, userId, "Northern Pike", 0.92,
            [new PredictionItem("Northern Pike", "Esox lucius", 0.92, 1)],
            "stored/fish.jpg");

        ContractEvents.FishIdentifiedEvent? published = null;
        bus.Publish(Arg.Do<ContractEvents.FishIdentifiedEvent>(e => published = e), Arg.Any<CancellationToken>());

        await publisher.PublishAsync(domainEvent);

        Assert.NotNull(published);
        Assert.Equal(predictionId, published!.PredictionId);
        Assert.Equal(observationId, published.ObservationId);
        Assert.Equal(userId, published.UserId);
        Assert.Equal("Northern Pike", published.TopSpeciesName);
        Assert.Equal(0.92, published.TopConfidence);
        Assert.Equal("stored/fish.jpg", published.ImageStorageKey);
        var prediction = Assert.Single(published.Predictions);
        Assert.Equal("Esox lucius", prediction.ScientificName);
        Assert.Equal(1, prediction.Rank);
    }

    [Fact]
    public async Task PublishAsync_UnrecognizedDomainEventType_DoesNotPublishAnything()
    {
        var bus = Substitute.For<IPublishEndpoint>();
        var publisher = new RabbitMQPublisher(bus);

        await publisher.PublishAsync(new OtherDomainEvent());

        Assert.Empty(bus.ReceivedCalls());
    }
}
