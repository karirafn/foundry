using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed class TestAggregateRoot(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
{
    public void RaiseEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}
