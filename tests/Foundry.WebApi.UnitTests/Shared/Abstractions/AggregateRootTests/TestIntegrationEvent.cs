using Foundry.Shared;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.AggregateRootTests;

internal sealed record TestIntegrationEvent(string Name) : IIntegrationEvent;
