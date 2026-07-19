using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.DockerAvailability;

internal sealed class DockerAvailabilityHealthCheckPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<DockerAvailabilityHealthCheckPublisher> logger) : IHealthCheckPublisher
{
    private const string DockerDaemonCheckName = "docker-daemon";

    private bool? _lastPublished;

    public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        if (!report.Entries.TryGetValue(DockerDaemonCheckName, out HealthReportEntry entry))
        {
            return;
        }

        bool available = entry.Status == HealthStatus.Healthy;

        if (_lastPublished is not null && _lastPublished == available)
        {
            return;
        }

        _lastPublished = available;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IIntegrationEventDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        await dispatcher.DispatchAsync([new DockerAvailabilityChanged(available)], cancellationToken);

        logger.LogInformation(
            "Docker availability changed: IsAvailable={IsAvailable}.",
            available);
    }
}
