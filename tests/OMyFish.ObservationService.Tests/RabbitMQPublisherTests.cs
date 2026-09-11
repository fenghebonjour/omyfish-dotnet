using MassTransit;
using NSubstitute;
using OMyFish.ObservationService.Domain.Events;
using OMyFish.ObservationService.Infrastructure.Messaging;
using OMyFish.Shared.BuildingBlocks.Domain;
using Xunit;
using ContractEvents = OMyFish.Shared.Contracts.Events;

namespace OMyFish.ObservationService.Tests;

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
    public async Task PublishAsync_ObservationCreatedEvent_PublishesTheMappedIntegrationEvent()
    {
        var bus = Substitute.For<IPublishEndpoint>();
        var publisher = new RabbitMQPublisher(bus);
        var observationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var observedAt = DateTime.UtcNow;
        var domainEvent = new ObservationCreatedEvent(
            observationId, userId, "Walleye", 45.5, -73.5, "stored/fish.jpg", observedAt);

        ContractEvents.ObservationCreatedEvent? published = null;
        bus.Publish(
            Arg.Do<ContractEvents.ObservationCreatedEvent>(e => published = e), Arg.Any<CancellationToken>());

        await publisher.PublishAsync(domainEvent);

        Assert.NotNull(published);
        Assert.Equal(observationId, published!.ObservationId);
        Assert.Equal(userId, published.UserId);
        Assert.Equal("Walleye", published.SpeciesName);
        Assert.Equal(45.5, published.Latitude);
        Assert.Equal(-73.5, published.Longitude);
        Assert.Equal("stored/fish.jpg", published.ImageStorageKey);
        Assert.Equal(observedAt, published.ObservedAt);
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
