using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed record TestDomainEvent(string Name) : IDomainEvent;
