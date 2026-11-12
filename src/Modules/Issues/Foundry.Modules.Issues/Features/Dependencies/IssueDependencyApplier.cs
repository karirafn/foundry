using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.Services;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Issues.Features.Dependencies;

/// <summary>
/// Applies an issue's detected open blockers: records <see cref="Issue.BlockedBy"/>, detects
/// cycles over the committed dependency graph augmented with the new edges, and performs the
/// <see cref="FreshQueuedIssue"/>/<see cref="BlockedIssue"/> and <see cref="TriageQueuedIssue"/>/<see cref="TriageBlockedIssue"/>
/// state transitions, sending a <see cref="DetectedIssue"/> to whichever of the triage pair fits (ADR 0102). The other
/// triage states only record their blockers, which decide where a ready verdict sends the issue. Shared by the
/// poll-path dependency pass and the claim-time blocker re-check so
/// both enforce the same rule: an issue with open blockers is never queued, for triage or for implementation.
/// </summary>
internal sealed class IssueDependencyApplier(
    DbContext db,
    IIssueQueries issueQueries,
    IDomainEventDispatcher dispatcher)
{
    /// <summary>
    /// Applies <paramref name="blockedBy"/> to the tracked <paramref name="issue"/> and commits
    /// the result. A cycle including the issue no longer suppresses the transition: an issue with
    /// open blockers is blocked whether or not a cycle exists, and
    /// <see cref="CircularDependencyDetected"/> is still raised.
    /// </summary>
    public async Task ApplyAsync(
        Issue issue,
        IReadOnlyList<int> blockedBy,
        CancellationToken cancellationToken)
    {
        // Query the committed graph before mutating BlockedBy so that cycle
        // detection sees a consistent snapshot. The current issue's new edges
        // are added manually below using the detected blockers.
        IReadOnlyList<DependencyEdge> committedGraph = await issueQueries.GetDependencyGraphAsync(
            issue.MonitoredRepositoryId,
            cancellationToken);

        IReadOnlyList<DependencyEdge> augmentedGraph = BuildAugmentedGraph(
            committedGraph,
            issue.IssueNumber,
            blockedBy);

        IReadOnlyList<int>? cycleIncludingIssue = DependencyCycleDetector.DetectCycles(augmentedGraph)
            .FirstOrDefault(cycle => cycle.Contains(issue.IssueNumber));

        // Apply the new BlockedBy value. From this point there is a single
        // save or transition — no intermediate SaveChangesAsync beforehand.
        issue.SetBlockedBy(blockedBy);

        switch (issue)
        {
            // An untriaged issue waits for its open blockers to close before it is triaged, so triage judges it
            // against the code they wrote (ADR 0102); without blockers it goes straight to triage.
            case DetectedIssue detected when issue.BlockedBy.Count > 0:
            {
                TriageBlockedIssue triageBlocked = detected.BlockTriage(issue.BlockedBy);
                await db.TransitionAsync(detected, triageBlocked, dispatcher, cancellationToken);
                break;
            }

            case DetectedIssue detected:
            {
                TriageQueuedIssue triageQueued = detected.QueueTriage();
                await db.TransitionAsync(detected, triageQueued, dispatcher, cancellationToken);
                break;
            }

            case TriageQueuedIssue triageQueued when issue.BlockedBy.Count > 0:
            {
                TriageBlockedIssue triageBlocked = triageQueued.Block(issue.BlockedBy);
                await db.TransitionAsync(triageQueued, triageBlocked, dispatcher, cancellationToken);
                break;
            }

            case TriageBlockedIssue triageBlocked when issue.BlockedBy.Count == 0:
            {
                TriageQueuedIssue triageQueued = triageBlocked.Unblock();
                await db.TransitionAsync(triageBlocked, triageQueued, dispatcher, cancellationToken);
                break;
            }

            case FreshQueuedIssue queued when issue.BlockedBy.Count > 0:
            {
                BlockedIssue blocked = queued.Block(issue.BlockedBy);
                await db.TransitionAsync(queued, blocked, dispatcher, cancellationToken);
                break;
            }

            case BlockedIssue blocked when issue.BlockedBy.Count == 0:
            {
                FreshQueuedIssue queued = blocked.Unblock();
                await db.TransitionAsync(blocked, queued, dispatcher, cancellationToken);
                break;
            }

            // A blocked issue with blockers still open stays blocked; a queued issue with none stays queued; any
            // other state (a running or failed triage, an issue awaiting input) only records BlockedBy, which decides
            // where a ready verdict sends it. Persist explicitly.
            default:
                await db.SaveChangesAsync(cancellationToken);
                break;
        }

        if (cycleIncludingIssue is not null)
        {
            await dispatcher.DispatchAsync(
                [new CircularDependencyDetected(issue.MonitoredRepositoryId, cycleIncludingIssue)],
                cancellationToken);
        }
    }

    private static List<DependencyEdge> BuildAugmentedGraph(
        IReadOnlyList<DependencyEdge> committedGraph,
        int currentIssueNumber,
        IReadOnlyList<int> newBlockedBy)
    {
        // Remove stale edges for the current issue and replace with the detected blockers.
        List<DependencyEdge> edges = committedGraph
            .Where(e => e.IssueNumber != currentIssueNumber)
            .ToList();

        foreach (int blocker in newBlockedBy)
        {
            edges.Add(new DependencyEdge(currentIssueNumber, blocker));
        }

        return edges;
    }
}
