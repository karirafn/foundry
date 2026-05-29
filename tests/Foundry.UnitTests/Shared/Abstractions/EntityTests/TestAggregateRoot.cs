namespace Foundry.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.Shared;

internal sealed class TestAggregateRoot(TestId id) : AggregateRoot<TestId>(id);
