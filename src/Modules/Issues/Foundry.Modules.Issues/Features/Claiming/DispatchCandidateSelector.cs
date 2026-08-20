using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Selects the best <see cref="DispatchCandidate"/> from all claimable issues across eligible
/// repositories, applying dispatch-order ordering and per-repository dispatch-info memoization.
/// </summary>
internal sealed class DispatchCandidateSelector(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IRepositoryEligibilityQuery repositoryEligibilityQuery)
{
    public async Task<SelectionOutcome> SelectAsync(CancellationToken cancellationToken)
    {
        List<MonitoredRepositoryId> claimableRepoIds = await db.Set<ClaimableIssue>()
            .Select(c => c.MonitoredRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (claimableRepoIds.Count == 0)
        {
            return new SelectionOutcome.NoCandidates();
        }

        Dictionary<MonitoredRepositoryId, int> positionByRepoId =
            await ResolveEligibleRepositoryPositionsAsync(
                claimableRepoIds.Select(id => id.Value).ToList(),
                cancellationToken);

        if (positionByRepoId.Count == 0)
        {
            return new SelectionOutcome.NoEligibleRepositories();
        }

        List<MonitoredRepositoryId> eligibleIds = positionByRepoId.Keys.ToList();

        List<ClaimableIssue> candidates = await db.Set<ClaimableIssue>()
            .Where(c => eligibleIds.Contains(c.MonitoredRepositoryId))
            .ToListAsync(cancellationToken);

        List<ClaimableIssue> ordered = candidates
            .OrderBy(c => DispatchOrderKey.For(c, positionByRepoId[c.MonitoredRepositoryId]))
            .ToList();

        Dictionary<MonitoredRepositoryId, RepositoryDispatchInfo?> dispatchInfoCache = [];
        int skipped = 0;

        foreach (ClaimableIssue candidate in ordered)
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

    private async Task<Dictionary<MonitoredRepositoryId, int>> ResolveEligibleRepositoryPositionsAsync(
        List<Guid> claimableRepoIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EligibleRepository> eligibleRepos = await repositoryEligibilityQuery
            .GetEligibleRepositoriesAsync(claimableRepoIds, cancellationToken);

        return eligibleRepos
            .Select(r => (Id: MonitoredRepositoryId.From(r.Id), r.Position))
            .ToDictionary(r => r.Id, r => r.Position);
    }
}
