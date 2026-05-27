namespace Foundry.WebApi.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.WebApi.Shared.Abstractions;

internal sealed class TestAggregateRoot(TestId id) : AggregateRoot<TestId>(id);
