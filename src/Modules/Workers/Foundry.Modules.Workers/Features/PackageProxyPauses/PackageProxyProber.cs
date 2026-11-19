using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.HostPauses;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.PackageProxyPauses;

/// <summary>
/// Probes the paused Package Source Proxy through the Publisher, from a container with a worker's <c>/etc/hosts</c>
/// entry, so the release takes the same network path as the worker check that tripped the pause. Reachable clears the
/// pause; unreachable or inconclusive waits for the next probe on the backoff.
/// </summary>
internal sealed class PackageProxyProber(
    DbContext dbContext,
    IPackageSourceProxyAddress proxyAddress,
    IWorkerHostEntryResolver hostEntryResolver,
    IPackageProxyProbe probe,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<PackageProxyProber> logger)
{
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        Uri proxyUrl = proxyAddress.WorkerBaseUrl;
        string extraHostEntry = await hostEntryResolver.ResolveAsync(cancellationToken);
        PackageProxyProbeOutcome outcome = await probe.ProbeAsync(proxyUrl, extraHostEntry, cancellationToken);

        switch (outcome)
        {
            case PackageProxyProbeOutcome.Reachable:
                await ClearAsync(proxyUrl, cancellationToken);
                return;

            case PackageProxyProbeOutcome.Unreachable unreachable:
                logger.LogInformation(
                    "Probe of the package proxy at {ProxyUrl}: still unreachable: {Detail}",
                    proxyUrl,
                    unreachable.Detail);
                break;

            case PackageProxyProbeOutcome.Inconclusive inconclusive:
                logger.LogWarning(
                    "Probe of the package proxy at {ProxyUrl} was inconclusive: {Reason}",
                    proxyUrl,
                    inconclusive.Reason);
                break;
        }

        await RecordUnreachableAsync(cancellationToken);
    }

    private async Task ClearAsync(Uri proxyUrl, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().FirstOrDefaultAsync(cancellationToken);

        // Resume All may have cleared the pause while the probe ran.
        if (settings is null || !settings.ClearPackageProxyPause())
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Probe of the package proxy at {ProxyUrl}: reachable; dispatch for repositories with package sources resumed.",
            proxyUrl);
        await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: false, logger, cancellationToken);
    }

    private async Task RecordUnreachableAsync(CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().FirstOrDefaultAsync(cancellationToken);

        if (settings is null || !settings.RecordPackageProxyProbeUnreachable(timeProvider.GetUtcNow()))
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: true, logger, cancellationToken);
    }
}
