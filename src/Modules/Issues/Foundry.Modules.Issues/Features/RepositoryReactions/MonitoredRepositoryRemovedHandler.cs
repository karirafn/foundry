using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.RepositoryReactions;

/// <summary>
/// Deletes every non-completed issue of a removed repository (ADR 0117): it can never be polled, dispatched or
/// published again, and its open issues return from the provider if the repository is added again. Completed issues
/// stay for History. An issue whose worker or Publisher is still running is kept, so the run's outcome never lands on
/// nothing; removal is refused while one exists, so only a race leaves one here. An in-flight issue whose run waits for
/// a publish retry holds no live process and is deleted. No per-issue events are raised, as for an untracked issue.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.MonitoredRepositoryRemoved")]
internal sealed class MonitoredRepositoryRemovedHandler(
    DbContext db,
    IRepositoryRemovalBlockers removalBlockers,
    ILogger<MonitoredRepositoryRemovedHandler> logger) : IIntegrationEventHandler<MonitoredRepositoryRemoved>
{
    public async Task HandleAsync(MonitoredRepositoryRemoved @event, CancellationToken cancellationToken)
    {
        IReadOnlyList<RemovalBlockingIssue> blocking =
            await removalBlockers.GetBlockingIssuesAsync([@event.RepositoryId], cancellationToken);
        HashSet<int> blockingNumbers = [.. blocking.Select(b => b.IssueNumber)];

        List<Issue> issues = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .Where(i => !(i is CompletedIssue))
            .ToListAsync(cancellationToken);

        foreach (RemovalBlockingIssue issue in blocking)
        {
            logger.LogInformation(
                "Repository {RepositoryId} was removed but issue {IssueNumber} is in state {State}; preserving record.",
                @event.RepositoryId,
                issue.IssueNumber,
                issue.State);
        }

        List<Issue> deletable = [.. issues.Where(issue => !blockingNumbers.Contains(issue.IssueNumber))];
        if (deletable.Count == 0)
        {
            return;
        }

        db.Set<Issue>().RemoveRange(deletable);
        await db.SaveChangesAsync(cancellationToken);
    }
}
