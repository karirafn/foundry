using Foundry.Shared;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed class TestAggregateRoot(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
{
    public void RaiseEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }

    public void RaiseIntegrationEvent(IIntegrationEvent integrationEvent)
    {
        AddIntegrationEvent(integrationEvent);
    }
}
