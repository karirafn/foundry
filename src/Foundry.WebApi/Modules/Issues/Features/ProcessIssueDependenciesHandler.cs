using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Infrastructure;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Modules.Issues.Features;

internal sealed class ProcessIssueDependenciesHandler(
    FoundryDbContext db,
    IIssuesModule issuesModule,
    IDomainEventDispatcher dispatcher) : IDomainEventHandler<IssueDependenciesDetected>
{
    public async Task HandleAsync(IssueDependenciesDetected @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            return;
        }

        issue.SetBlockedBy(@event.BlockedByIssueNumbers);
        await db.SaveChangesAsync(cancellationToken);

        IReadOnlyList<DependencyEdge> graph = await issuesModule.GetDependencyGraphAsync(
            @event.MonitoredRepositoryId,
            cancellationToken);

        IReadOnlyList<IReadOnlyList<int>> cycles = DependencyCycleDetector.DetectCycles(graph);

        IReadOnlyList<int>? cycleIncludingIssue = cycles
            .FirstOrDefault(cycle => cycle.Contains(@event.IssueNumber));

        if (cycleIncludingIssue is not null)
        {
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
                await db.TransitionAsync(detected, queued, cancellationToken);
                await dispatcher.DispatchAsync(detected.DomainEvents, cancellationToken);
                break;
            }

            case DetectedIssue detected:
            {
                BlockedIssue blocked = detected.Block(issue.BlockedBy);
                await db.TransitionAsync(detected, blocked, cancellationToken);
                await dispatcher.DispatchAsync(detected.DomainEvents, cancellationToken);
                break;
            }

            case QueuedIssue queued when issue.BlockedBy.Count > 0:
            {
                BlockedIssue blocked = queued.Block(issue.BlockedBy);
                await db.TransitionAsync(queued, blocked, cancellationToken);
                await dispatcher.DispatchAsync(queued.DomainEvents, cancellationToken);
                break;
            }

            case BlockedIssue blocked when issue.BlockedBy.Count == 0:
            {
                QueuedIssue queued = blocked.Unblock();
                await db.TransitionAsync(blocked, queued, cancellationToken);
                await dispatcher.DispatchAsync(blocked.DomainEvents, cancellationToken);
                break;
            }
        }
    }
}
