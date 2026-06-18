namespace OMyFish.Shared.BuildingBlocks.Domain;

public abstract class AggregateRoot<TId>
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
    {
        Id = id;
    }

    public TId Id { get; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RegisterEvent(DomainEvent @event) => _domainEvents.Add(@event);

    public IReadOnlyList<DomainEvent> PullDomainEvents()
    {
        var events = _domainEvents.ToList().AsReadOnly();
        _domainEvents.Clear();
        return events;
    }
}
