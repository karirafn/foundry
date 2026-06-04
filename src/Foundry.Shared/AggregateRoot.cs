namespace Foundry.Shared;

public abstract class AggregateRoot<TId> : Entity<TId>, IDomainEventSource
    where TId : struct, IStronglyTypedId<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<IIntegrationEvent> _integrationEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public IReadOnlyList<IIntegrationEvent> IntegrationEvents => _integrationEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    protected void AddIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        _integrationEvents.Add(integrationEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public void ClearIntegrationEvents()
    {
        _integrationEvents.Clear();
    }
}
