using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Selects the best <see cref="DispatchCandidate"/> from all claimable issues across eligible
/// repositories, applying capacity-aware ordering (ADR 0074) and per-repository dispatch-info
/// memoization. Each fresh, triage, revision and continuation candidate is re-checked by
/// <see cref="ClaimTimeRecheck"/> before it is selected; a candidate that gained a blocker, or a Fresh candidate whose
/// branch already had work and was requeued as a Continuation, has left the queue, so the capacity-aware order is
/// re-walked without it. Repositories on a paused provider host
/// (<see cref="DispatchPauseState.PausedHosts"/>), and while the package proxy is paused
/// (<see cref="DispatchPauseState.PackageProxyPause"/>) repositories that declare Package Sources, are dropped before any
/// candidate is considered.
/// </summary>
internal sealed class DispatchCandidateSelector(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IRepositoryEligibilityQuery repositoryEligibilityQuery,
    InFlightWorkerCountQuery inFlightWorkerCountQuery,
    ClaimTimeRecheck claimTimeRecheck,
    IDispatchPauseQueries dispatchPauseQueries,
    ILogger<DispatchCandidateSelector> logger)
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

        IReadOnlyList<EligibleRepository> allEligibleRepos = await repositoryEligibilityQuery
            .GetEligibleRepositoriesAsync(
                claimableRepoIds.Select(id => id.Value).ToList(),
                cancellationToken);

        if (allEligibleRepos.Count == 0)
        {
            return new SelectionOutcome.NoEligibleRepositories();
        }

        // A host pause holds every queued kind on its host, and a package-proxy pause every queued kind of a repository
        // that declares Package Sources: they wait in the queue rather than each dispatching into an unreachable
        // endpoint and failing.
        DispatchPauseState pauseState = await dispatchPauseQueries.GetDispatchPauseStateAsync(cancellationToken);
        List<EligibleRepository> eligibleRepos = allEligibleRepos
            .Where(repo => !IsOnPausedHost(repo, pauseState.PausedHosts))
            .Where(repo => pauseState.PackageProxyPause is null || !repo.DeclaresPackageSources)
            .ToList();

        if (eligibleRepos.Count == 0)
        {
            logger.LogDebug(
                "Every repository with queued work is on a paused provider host ({PausedHosts}) or held by the package-proxy pause ({PackageProxyPaused}); nothing to claim.",
                string.Join(", ", pauseState.PausedHosts.Select(h => h.Host)),
                pauseState.PackageProxyPause is not null);
            return new SelectionOutcome.NoCandidates();
        }

        List<MonitoredRepositoryId> eligibleIds = eligibleRepos
            .Select(r => MonitoredRepositoryId.From(r.Id))
            .ToList();

        List<QueuedIssue> candidates = await db.Set<QueuedIssue>()
            .Where(c => eligibleIds.Contains(c.MonitoredRepositoryId))
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<MonitoredRepositoryId, int> inFlightCounts =
            await inFlightWorkerCountQuery.GetCountsAsync(cancellationToken);

        DispatchHeadroom headroom = DispatchHeadroom.ForClaim(eligibleRepos, inFlightCounts);

        Dictionary<MonitoredRepositoryId, int> positionByRepo = eligibleRepos
            .ToDictionary(r => MonitoredRepositoryId.From(r.Id), r => r.Position);

        List<(QueuedIssue Issue, DispatchOrderKey Key)> keyedCandidates = candidates
            .Select(c => (c, DispatchOrderKey.For(c, positionByRepo[c.MonitoredRepositoryId])))
            .ToList();

        IReadOnlyList<QueuedIssue> ordered = CapacityAwareDispatchOrder.Order(keyedCandidates, headroom);

        if (ordered.Count == 0)
        {
            return new SelectionOutcome.AllRepositoriesSaturated();
        }

        Dictionary<MonitoredRepositoryId, RepositoryDispatchInfo?> dispatchInfoCache = [];
        HashSet<QueuedIssue> skippedCandidates = [];
        int unresolvable = 0;
        int blocked = 0;
        int readFailed = 0;
        int branchAdopted = 0;
        bool reordered;

        do
        {
            reordered = false;

            foreach (QueuedIssue candidate in ordered)
            {
                if (skippedCandidates.Contains(candidate))
                {
                    continue;
                }

                MonitoredRepositoryId repoId = candidate.MonitoredRepositoryId;

                if (!dispatchInfoCache.TryGetValue(repoId, out RepositoryDispatchInfo? dispatchInfo))
                {
                    dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(repoId, cancellationToken);
                    dispatchInfoCache[repoId] = dispatchInfo;
                }

                if (dispatchInfo is null)
                {
                    skippedCandidates.Add(candidate);
                    unresolvable++;
                    continue;
                }

                // Claim-time blocker re-check: the recorded BlockedBy may predate a blocker added
                // on the provider (GitLab sends no webhook for issue links), so re-read it live. An issue with open
                // blockers starts no run (ADR 0102): fresh work, triage, and revisions and continuations on an existing
                // branch or pull request whatever their trigger, so every path into those queues meets this gate. It
                // also covers an issue created with blockers before its first dependency pass. A queued review is not
                // gated: the code-owned pipeline is never interrupted by a blocker. A fresh candidate whose branch
                // already has work is requeued as a continuation on it, which the next pass re-checks.
                if (candidate is FreshQueuedIssue or TriageQueuedIssue or RevisionQueuedIssue or ContinuationQueuedIssue)
                {
                    ClaimTimeRecheckOutcome check = await claimTimeRecheck.CheckAsync(candidate, cancellationToken);

                    if (check == ClaimTimeRecheckOutcome.ReadFailed)
                    {
                        // No state change on a failed read: the candidate stays queued and the
                        // next dispatch tick retries it.
                        skippedCandidates.Add(candidate);
                        readFailed++;
                        continue;
                    }

                    if (check is ClaimTimeRecheckOutcome.Blocked or ClaimTimeRecheckOutcome.BranchHasWork)
                    {
                        // The issue has left the queue it was selected from (blocked, or requeued as a Continuation that a
                        // later pass picks up), so re-walk the capacity-aware order without it: the next same-repository
                        // issue may only now fit within the repository's headroom.
                        if (check == ClaimTimeRecheckOutcome.Blocked)
                        {
                            blocked++;
                        }
                        else
                        {
                            branchAdopted++;
                        }

                        keyedCandidates.RemoveAll(k => ReferenceEquals(k.Issue, candidate));
                        ordered = CapacityAwareDispatchOrder.Order(keyedCandidates, headroom);
                        reordered = true;
                        break;
                    }
                }

                return new SelectionOutcome.Selected(new DispatchCandidate(candidate, dispatchInfo));
            }
        }
        while (reordered);

        if (blocked == 0 && readFailed == 0 && branchAdopted == 0)
        {
            return new SelectionOutcome.AllCandidatesUnresolvable(unresolvable);
        }

        return new SelectionOutcome.AllCandidatesSkipped(unresolvable, blocked, readFailed, branchAdopted);
    }

    private static bool IsOnPausedHost(EligibleRepository repo, IReadOnlyList<PausedHost> pausedHosts) =>
        pausedHosts.Any(paused => string.Equals(paused.Host, repo.Host, StringComparison.OrdinalIgnoreCase));
}
