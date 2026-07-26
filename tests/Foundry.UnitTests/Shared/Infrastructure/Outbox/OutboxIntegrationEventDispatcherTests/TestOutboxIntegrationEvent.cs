using Foundry.Shared;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxIntegrationEventDispatcherTests;

internal sealed record TestOutboxIntegrationEvent(string Value) : IIntegrationEvent;
