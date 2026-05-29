using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

internal readonly record struct TestAggregateRootId(Guid Value) : IStronglyTypedId<TestAggregateRootId>
{
    public static TestAggregateRootId From(Guid value) => new(value);
}
