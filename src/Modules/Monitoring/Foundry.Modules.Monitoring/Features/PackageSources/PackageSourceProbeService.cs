using Foundry.Modules.Monitoring.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Probes each source a credential change requested (<see cref="PackageSourceProbeRequests"/>), one at a time and
/// each in its own scope. A probe that fails is logged; the source's next scheduled probe retries it.
/// </summary>
internal sealed class PackageSourceProbeService(
    PackageSourceProbeRequests requests,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PackageSourceProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (PackageSourceId id in requests.Reader.ReadAllAsync(stoppingToken))
            {
                await ProbeAsync(id, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    internal async Task ProbeAsync(PackageSourceId id, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            PackageSourceProbing probing = scope.ServiceProvider.GetRequiredService<PackageSourceProbing>();
            await probing.ProbeSourceNowAsync(id, timeProvider.GetUtcNow(), cancellationToken);
        }
#pragma warning disable CA1031 // A failed probe must not stop the service; the next scheduled probe retries the source.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Probing package source {Id} after its credential changed failed.", id.Value);
        }
    }
}
