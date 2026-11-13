using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.HostPauses;

/// <summary>
/// Probes one paused provider host through the Publisher, so the release uses the same network path and the same
/// <c>classify_error</c> as the trip. Up to <see cref="MaxRepositoriesPerProbe"/> repositories on the host are tried
/// one after another, most recently prepared first; the first reachable one clears the pause. When none is reachable,
/// or none could be probed, the pause waits for its next probe.
/// </summary>
internal sealed class HostProber(
    DbContext dbContext,
    IHostRepositoryQueries hostRepositoryQueries,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IPublisher publisher,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<HostProber> logger)
{
    /// <summary>
    /// More than one, so a single broken repository cannot hold its whole host paused; few, so a long outage costs
    /// only a few short containers per probe.
    /// </summary>
    internal const int MaxRepositoriesPerProbe = 3;

    public async Task ProbeAsync(string host, CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitoredRepositoryId> repositories = await RankRepositoriesAsync(host, cancellationToken);
        int probed = 0;

        foreach (MonitoredRepositoryId repositoryId in repositories)
        {
            if (probed == MaxRepositoriesPerProbe)
            {
                break;
            }

            // The token is resolved as Prepare resolves it; a repository without a usable one cannot be probed.
            RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(repositoryId, cancellationToken);

            if (dispatchInfo?.AccountToken is not { Length: > 0 } gitPat)
            {
                continue;
            }

            probed++;
            HostProbeOutcome outcome = await publisher.ProbeAsync(new HostProbeRequest(dispatchInfo.CloneUrl, gitPat), cancellationToken);

            switch (outcome)
            {
                case HostProbeOutcome.Reachable:
                    await ClearAsync(host, dispatchInfo.RepositorySlug, cancellationToken);
                    return;

                case HostProbeOutcome.Unreachable unreachable:
                    logger.LogInformation(
                        "Probe of {Host} through {RepositorySlug}: still unreachable: {Detail}",
                        host,
                        dispatchInfo.RepositorySlug,
                        unreachable.Detail);
                    break;

                case HostProbeOutcome.Inconclusive inconclusive:
                    logger.LogWarning(
                        "Probe of {Host} through {RepositorySlug} was inconclusive: {Reason}",
                        host,
                        dispatchInfo.RepositorySlug,
                        inconclusive.Reason);
                    break;
            }
        }

        if (probed == 0)
        {
            logger.LogWarning(
                "Probe of {Host} was inconclusive: no monitored repository on the host has a usable token to probe with. "
                + "Resume All clears the pause.",
                host);
        }

        await RecordUnreachableAsync(host, cancellationToken);
    }

    /// <summary>
    /// The host's repositories, most recent successful Prepare first; those never prepared follow in their dispatch
    /// order.
    /// </summary>
    private async Task<IReadOnlyList<MonitoredRepositoryId>> RankRepositoriesAsync(string host, CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitoredRepositoryId> onHost = await hostRepositoryQueries.GetRepositoryIdsOnHostAsync(host, cancellationToken);

        if (onHost.Count == 0)
        {
            return [];
        }

        List<MonitoredRepositoryId?> candidateIds = [.. onHost.Select(id => (MonitoredRepositoryId?)id)];

        // Ranked in memory: SQLite cannot order or aggregate DateTimeOffset values.
        var preparedRuns = await dbContext.Set<WorkerRun>()
            .AsNoTracking()
            .Where(run => candidateIds.Contains(run.PreparedRepositoryId))
            .Select(run => new { run.PreparedRepositoryId, run.CreatedAt })
            .ToListAsync(cancellationToken);

        Dictionary<MonitoredRepositoryId, DateTimeOffset> lastPreparedAt = preparedRuns
            .GroupBy(run => run.PreparedRepositoryId!.Value)
            .ToDictionary(runs => runs.Key, runs => runs.Max(run => run.CreatedAt));

        return
        [
            .. onHost
                .Select((id, order) => (Id: id, Order: order))
                .OrderByDescending(repo => lastPreparedAt.TryGetValue(repo.Id, out DateTimeOffset at) ? at : DateTimeOffset.MinValue)
                .ThenBy(repo => repo.Order)
                .Select(repo => repo.Id),
        ];
    }

    private async Task ClearAsync(string host, string repositorySlug, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().FirstOrDefaultAsync(cancellationToken);

        // Resume All may have cleared the pause while the probe ran.
        if (settings is null || !settings.ClearHostPause(host))
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Probe of {Host} through {RepositorySlug}: reachable; dispatch to the host resumed.",
            host,
            repositorySlug);
        await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: false, logger, cancellationToken);
    }

    private async Task RecordUnreachableAsync(string host, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().FirstOrDefaultAsync(cancellationToken);

        if (settings is null || !settings.RecordProbeUnreachable(host, timeProvider.GetUtcNow()))
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: true, logger, cancellationToken);
    }
}
