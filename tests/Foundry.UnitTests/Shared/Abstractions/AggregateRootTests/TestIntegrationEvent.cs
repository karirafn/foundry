using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed record TestIntegrationEvent(string Name) : IIntegrationEvent;
