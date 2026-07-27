using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Queries;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.Services;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

internal sealed class ProcessIssueDependenciesHandler(
    DbContext db,
    IIssueQueries issueQueries,
    IDomainEventDispatcher dispatcher,
    ILogger<ProcessIssueDependenciesHandler> logger) : IIntegrationEventHandler<IssueDependenciesDetected>
{
    public async Task HandleAsync(IssueDependenciesDetected @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            logger.LogWarning(
                "Issue {IssueNumber} not found for repository {RepositoryId} during dependency processing",
                @event.IssueNumber,
                @event.MonitoredRepositoryId);
            return;
        }

        // Query the committed graph before mutating BlockedBy so that cycle
        // detection sees a consistent snapshot. The current issue's new edges
        // are added manually below using the event payload.
        IReadOnlyList<DependencyEdge> committedGraph = await issueQueries.GetDependencyGraphAsync(
            @event.MonitoredRepositoryId,
            cancellationToken);

        IReadOnlyList<DependencyEdge> augmentedGraph = BuildAugmentedGraph(
            committedGraph,
            @event.IssueNumber,
            @event.BlockedByIssueNumbers);

        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(augmentedGraph);

        IReadOnlyList<int>? cycleIncludingIssue = cycles
            .FirstOrDefault(cycle => cycle.Contains(@event.IssueNumber));

        // Apply the new BlockedBy value. From this point there is a single
        // save or transition — no intermediate SaveChangesAsync beforehand.
        issue.SetBlockedBy(@event.BlockedByIssueNumbers);

        if (cycleIncludingIssue is not null)
        {
            // Persist BlockedBy update without a state transition, then report the cycle.
            await db.SaveChangesAsync(cancellationToken);
            await dispatcher.DispatchAsync(
                [new CircularDependencyDetected(@event.MonitoredRepositoryId, cycleIncludingIssue)],
                cancellationToken);
            return;
        }

        switch (issue)
        {
            case DetectedIssue detected when issue.BlockedBy.Count == 0:
            {
                QueuedIssue queued = detected.Enqueue();
                await db.TransitionAsync(detected, queued, dispatcher, cancellationToken);
                break;
            }

            case DetectedIssue detected:
            {
                BlockedIssue blocked = detected.Block(issue.BlockedBy);
                await db.TransitionAsync(detected, blocked, dispatcher, cancellationToken);
                break;
            }

            case QueuedIssue queued when issue.BlockedBy.Count > 0:
            {
                BlockedIssue blocked = queued.Block(issue.BlockedBy);
                await db.TransitionAsync(queued, blocked, dispatcher, cancellationToken);
                break;
            }

            case BlockedIssue blocked when issue.BlockedBy.Count == 0:
            {
                QueuedIssue queued = blocked.Unblock();
                await db.TransitionAsync(blocked, queued, dispatcher, cancellationToken);
                break;
            }

            // BlockedIssue with still-existing blockers: remains blocked, persist the updated BlockedBy.
            case BlockedIssue when issue.BlockedBy.Count > 0:
                await db.SaveChangesAsync(cancellationToken);
                break;

            // QueuedIssue with no new blockers: already queued, persist (SetBlockedBy([]) is a no-op but save is explicit).
            case QueuedIssue when issue.BlockedBy.Count == 0:
                await db.SaveChangesAsync(cancellationToken);
                break;
        }
    }

    private static List<DependencyEdge> BuildAugmentedGraph(
        IReadOnlyList<DependencyEdge> committedGraph,
        int currentIssueNumber,
        IReadOnlyList<int> newBlockedBy)
    {
        // Remove stale edges for the current issue and replace with the new event payload.
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
