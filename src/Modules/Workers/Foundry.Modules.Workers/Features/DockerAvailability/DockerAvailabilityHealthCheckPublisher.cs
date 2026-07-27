using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Events;
using Foundry.Modules.Workers.Features.Health;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal sealed class DockerAvailabilityHealthCheckPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<DockerAvailabilityHealthCheckPublisher> logger) : IHealthCheckPublisher
{

    private bool? _lastPublished;

    public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        if (!report.Entries.TryGetValue(DockerDaemonHealthCheck.CheckName, out HealthReportEntry entry))
        {
            return;
        }

        bool available = entry.Status == HealthStatus.Healthy;

        if (_lastPublished is not null && _lastPublished == available)
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        // DockerAvailabilityChanged has only one consumer: DockerAvailabilityChangedBroadcastHandler,
        // which mutates in-memory state and sends a SignalR notification. There is no durable DB
        // consumer, so routing through the outbox would require an unnecessary DB save and add
        // relay latency to a purely transient signal. Deliver directly via IIntegrationEventProcessor.
        IIntegrationEventProcessor processor =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();

        await processor.ProcessAsync(Guid.NewGuid(), new DockerAvailabilityChanged(available), cancellationToken);

        _lastPublished = available;

        logger.LogInformation(
            "Docker availability changed: IsAvailable={IsAvailable}.",
            available);
    }
}
