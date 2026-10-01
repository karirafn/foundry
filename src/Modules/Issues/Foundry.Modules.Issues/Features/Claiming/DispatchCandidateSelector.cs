using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Selects the best <see cref="DispatchCandidate"/> from all claimable issues across eligible
/// repositories, applying capacity-aware ordering (ADR 0074) and per-repository dispatch-info
/// memoization.
/// </summary>
internal sealed class DispatchCandidateSelector(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IRepositoryEligibilityQuery repositoryEligibilityQuery,
    InFlightWorkerCountQuery inFlightWorkerCountQuery)
{
    public async Task<SelectionOutcome> SelectAsync(CancellationToken cancellationToken)
    {
        List<MonitoredRepositoryId> claimableRepoIds = await db.Set<QueuedIssue>()
            .Select(c => c.MonitoredRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (claimableRepoIds.Count == 0)
        {
            return new SelectionOutcome.NoCandidates();
        }

        IReadOnlyList<EligibleRepository> eligibleRepos = await repositoryEligibilityQuery
            .GetEligibleRepositoriesAsync(
                claimableRepoIds.Select(id => id.Value).ToList(),
                cancellationToken);

        if (eligibleRepos.Count == 0)
        {
            return new SelectionOutcome.NoEligibleRepositories();
        }

        List<MonitoredRepositoryId> eligibleIds = eligibleRepos
            .Select(r => MonitoredRepositoryId.From(r.Id))
            .ToList();

        List<QueuedIssue> candidates = await db.Set<QueuedIssue>()
            .Where(c => eligibleIds.Contains(c.MonitoredRepositoryId))
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlightCounts =
            await inFlightWorkerCountQuery.GetCountsAsync(cancellationToken);

        Dictionary<MonitoredRepositoryId, int> headroomByRepo = eligibleRepos
            .Select(r =>
            {
                MonitoredRepositoryId id = MonitoredRepositoryId.From(r.Id);
                inFlightCounts.TryGetValue(id, out int inFlight);
                int headroom = Math.Max(0, r.MaxConcurrentWorkers - inFlight);
                return (Id: id, Headroom: headroom);
            })
            .ToDictionary(r => r.Id, r => r.Headroom);

        Dictionary<MonitoredRepositoryId, int> positionByRepo = eligibleRepos
            .ToDictionary(r => MonitoredRepositoryId.From(r.Id), r => r.Position);

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedCandidates = candidates
            .Select(c => (c, DispatchOrderKey.For(c, positionByRepo[c.MonitoredRepositoryId])))
            .ToList();

        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedCandidates, headroomByRepo);

        if (ordered.Count == 0)
        {
            return new SelectionOutcome.AllRepositoriesSaturated();
        }

        Dictionary<MonitoredRepositoryId, RepositoryDispatchInfo?> dispatchInfoCache = [];
        int skipped = 0;

        foreach (QueuedIssue candidate in ordered)
        {
            MonitoredRepositoryId repoId = candidate.MonitoredRepositoryId;

            if (!dispatchInfoCache.TryGetValue(repoId, out RepositoryDispatchInfo? dispatchInfo))
            {
                dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(repoId, cancellationToken);
                dispatchInfoCache[repoId] = dispatchInfo;
            }

            if (dispatchInfo is null)
            {
                skipped++;
                continue;
            }

            return new SelectionOutcome.Selected(new DispatchCandidate(candidate, dispatchInfo));
        }

        return new SelectionOutcome.AllCandidatesUnresolvable(skipped);
    }
}
