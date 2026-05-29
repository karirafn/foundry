using Foundry.Shared;

namespace Foundry.WebApi.UnitTests.Shared.Abstractions.ServiceCollectionExtensionsTests;

internal sealed record DiTestIntegrationEvent(string Payload) : IIntegrationEvent;

internal sealed class DiTestIntegrationEventHandler : IIntegrationEventHandler<DiTestIntegrationEvent>
{
    public Task HandleAsync(DiTestIntegrationEvent @event, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
