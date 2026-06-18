using MassTransit;
using OMyFish.ObservationService.Domain.Events;
using OMyFish.Shared.BuildingBlocks.Domain;
using OMyFish.Shared.BuildingBlocks.Messaging;
using ContractEvents = OMyFish.Shared.Contracts.Events;

namespace OMyFish.ObservationService.Infrastructure.Messaging;

public class RabbitMQPublisher : IMessagePublisher
{
    private readonly IPublishEndpoint _bus;

    public RabbitMQPublisher(IPublishEndpoint bus) => _bus = bus;

    public async Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        if (@event is not ObservationCreatedEvent obsEvent) return;

        var integrationEvent = new ContractEvents.ObservationCreatedEvent(
            obsEvent.ObservationId,
            obsEvent.UserId,
            obsEvent.SpeciesName,
            obsEvent.Latitude,
            obsEvent.Longitude,
            obsEvent.ImageStorageKey,
            obsEvent.ObservedAt);

        await _bus.Publish(integrationEvent, ct);
    }
}
