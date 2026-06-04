namespace Foundry.Shared;

public interface IDomainEventSource
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
