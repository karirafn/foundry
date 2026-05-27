namespace Foundry.WebApi.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.WebApi.Shared.Abstractions;

internal sealed class TestEntity(TestId id) : Entity<TestId>(id);
