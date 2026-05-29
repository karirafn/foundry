using Foundry.Shared;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed record TestDomainEvent(string Name) : IDomainEvent;
