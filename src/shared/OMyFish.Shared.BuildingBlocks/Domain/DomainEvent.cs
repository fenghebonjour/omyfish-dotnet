namespace OMyFish.Shared.BuildingBlocks.Domain;

public abstract record DomainEvent(
    Guid EventId,
    DateTime OccurredOn,
    string EventType)
{
    protected DomainEvent(string eventType)
        : this(Guid.NewGuid(), DateTime.UtcNow, eventType) { }
}
