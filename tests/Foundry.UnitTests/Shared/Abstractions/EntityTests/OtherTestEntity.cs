namespace Foundry.UnitTests.Shared.Abstractions.EntityTests;

using Foundry.Shared;

internal sealed class OtherTestEntity(TestId id) : Entity<TestId>(id);
