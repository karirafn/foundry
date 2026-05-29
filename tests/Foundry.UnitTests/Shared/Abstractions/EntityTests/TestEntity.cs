namespace Foundry.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.Shared;

internal sealed class TestEntity(TestId id) : Entity<TestId>(id);
