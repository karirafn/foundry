using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.Dependencies;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.Claiming;

/// <summary>
/// Selects the best <see cref="DispatchCandidate"/> from all claimable issues across eligible
/// repositories, applying capacity-aware ordering (ADR 0074) and per-repository dispatch-info
/// memoization. Each <see cref="FreshQueuedIssue"/> and <see cref="TriageQueuedIssue"/> candidate's open blockers are re-read from the
/// provider before it is selected; a candidate that gained a blocker is transitioned to <see cref="BlockedIssue"/> or
/// <see cref="TriageBlockedIssue"/> and the next candidate in dispatch order is tried. Repositories on a paused provider host
/// (<see cref="DispatchPauseState.PausedHosts"/>) are dropped before any candidate is considered.
/// </summary>
internal sealed class DispatchCandidateSelector(
    DbContext db,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IRepositoryEligibilityQuery repositoryEligibilityQuery,
    InFlightWorkerCountQuery inFlightWorkerCountQuery,
    IIssueBlockerQueries issueBlockerQueries,
    IssueDependencyApplier dependencyApplier,
    IGlobalSettingsQueries settingsQueries,
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

        // A host pause holds every queued kind on its host: they wait in the queue rather than each dispatching
        // into an unreachable provider and failing.
        DispatchPauseState pauseState = await settingsQueries.GetDispatchPauseStateAsync(cancellationToken);
        List<EligibleRepository> eligibleRepos = allEligibleRepos
            .Where(repo => !IsOnPausedHost(repo, pauseState.PausedHosts))
            .ToList();

        if (eligibleRepos.Count == 0)
        {
            logger.LogDebug(
                "Every repository with queued work is on a paused provider host ({PausedHosts}); nothing to claim.",
                string.Join(", ", pauseState.PausedHosts.Select(h => h.Host)));
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
        int blockerReadFailed = 0;
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
                // on the provider (GitLab sends no webhook for issue links), so re-read it live for
                // fresh work and for triage, which waits for open blockers to close (ADR 0102) — this also covers an
                // issue created with blockers before its first dependency pass. Revisions and continuations run on an
                // existing branch/PR and are not
                // re-gated.
                if (candidate is FreshQueuedIssue or TriageQueuedIssue)
                {
                    BlockerCheck check = await CheckBlockersAsync(candidate, cancellationToken);

                    if (check == BlockerCheck.ReadFailed)
                    {
                        // No state change on a failed read: the candidate stays queued and the
                        // next dispatch tick retries it.
                        skippedCandidates.Add(candidate);
                        blockerReadFailed++;
                        continue;
                    }

                    if (check == BlockerCheck.Blocked)
                    {
                        // The issue is now blocked and no longer holds a dispatch slot, so
                        // re-walk the capacity-aware order without it: the next same-repository
                        // issue may only now fit within the repository's headroom.
                        blocked++;
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

        if (blocked == 0 && blockerReadFailed == 0)
        {
            return new SelectionOutcome.AllCandidatesUnresolvable(unresolvable);
        }

        return new SelectionOutcome.AllCandidatesSkipped(unresolvable, blocked, blockerReadFailed);
    }

    private async Task<BlockerCheck> CheckBlockersAsync(
        QueuedIssue candidate,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<int>> blockers = await issueBlockerQueries.GetOpenBlockersAsync(
            candidate.MonitoredRepositoryId,
            candidate.IssueNumber,
            cancellationToken);

        if (blockers is not Result<IReadOnlyList<int>>.Success blockersSuccess)
        {
            Error error = ((Result<IReadOnlyList<int>>.Failure)blockers).Error;
            logger.LogWarning(
                "Skipping issue #{IssueNumber} for this dispatch: its open blockers could not be read ({ErrorCode}).",
                candidate.IssueNumber,
                error.Code);
            return BlockerCheck.ReadFailed;
        }

        if (blockersSuccess.Value.Count == 0)
        {
            return BlockerCheck.Clear;
        }

        // Transition to BlockedIssue or TriageBlockedIssue (committed) so the issue is not re-selected on the next tick
        // or on redelivery of this capacity event.
        await dependencyApplier.ApplyAsync(candidate, blockersSuccess.Value, cancellationToken);
        logger.LogInformation(
            "Issue #{IssueNumber} gained open blocker(s) {BlockedBy} since the last dependency pass; blocked instead of claimed.",
            candidate.IssueNumber,
            blockersSuccess.Value);
        return BlockerCheck.Blocked;
    }

    private static bool IsOnPausedHost(EligibleRepository repo, IReadOnlyList<PausedHost> pausedHosts) =>
        pausedHosts.Any(paused => string.Equals(paused.Host, repo.Host, StringComparison.OrdinalIgnoreCase));

    private enum BlockerCheck
    {
        Clear,
        Blocked,
        ReadFailed,
    }
}
