using MassTransit;
using OMyFish.Shared.BuildingBlocks.Domain;
using OMyFish.Shared.BuildingBlocks.Messaging;
using OMyFish.SpeciesService.Domain.Events;
using ContractEvents = OMyFish.Shared.Contracts.Events;

namespace OMyFish.SpeciesService.Infrastructure.Messaging;

public class RabbitMQPublisher : IMessagePublisher
{
    private readonly IPublishEndpoint _bus;

    public RabbitMQPublisher(IPublishEndpoint bus) => _bus = bus;

    public async Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        if (@event is not FishIdentifiedEvent fishEvent) return;

        var integrationEvent = new ContractEvents.FishIdentifiedEvent(
            fishEvent.PredictionId,
            fishEvent.ObservationId,
            fishEvent.UserId,
            fishEvent.TopSpeciesName,
            fishEvent.TopConfidence,
            fishEvent.Predictions
                .Select(p => new ContractEvents.PredictionResult(
                    p.SpeciesName, p.ScientificName, p.Confidence, p.Rank))
                .ToList(),
            fishEvent.ImageStorageKey);

        await _bus.Publish(integrationEvent, ct);
    }
}
