using OMyFish.Shared.BuildingBlocks.Domain;

namespace OMyFish.Shared.BuildingBlocks.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(DomainEvent @event, CancellationToken ct = default);
}
