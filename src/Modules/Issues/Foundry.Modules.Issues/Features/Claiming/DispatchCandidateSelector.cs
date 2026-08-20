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
        List<ClaimableIssue> allCandidates = await db.Set<ClaimableIssue>()
            .ToListAsync(cancellationToken);

        if (allCandidates.Count == 0)
        {
            return new SelectionOutcome.NoCandidates();
        }

        Dictionary<MonitoredRepositoryId, int> positionByRepoId =
            await ResolveEligibleRepositoryPositionsAsync(allCandidates, cancellationToken);

        if (positionByRepoId.Count == 0)
        {
            return new SelectionOutcome.NoEligibleRepositories();
        }

        List<ClaimableIssue> eligibleCandidates = allCandidates
            .Where(c => positionByRepoId.ContainsKey(c.MonitoredRepositoryId))
            .ToList();

        if (eligibleCandidates.Count == 0)
        {
            return new SelectionOutcome.NoCandidates();
        }

        List<ClaimableIssue> ordered = eligibleCandidates
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
        List<ClaimableIssue> candidates,
        CancellationToken cancellationToken)
    {
        List<Guid> rawIds = candidates
            .Select(c => c.MonitoredRepositoryId.Value)
            .Distinct()
            .ToList();

        IReadOnlyList<EligibleRepository> eligibleRepos = await repositoryEligibilityQuery
            .GetEligibleRepositoriesAsync(rawIds, cancellationToken);

        return eligibleRepos
            .Select(r => (Id: MonitoredRepositoryId.From(r.Id), r.Position))
            .ToDictionary(r => r.Id, r => r.Position);
    }
}
