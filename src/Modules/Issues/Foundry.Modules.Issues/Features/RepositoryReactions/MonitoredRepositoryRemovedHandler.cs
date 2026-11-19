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
/// stay for History. An issue whose worker is still running is kept, so the run's outcome never lands on nothing.
/// No per-issue events are raised, as for an untracked issue.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.MonitoredRepositoryRemoved")]
internal sealed class MonitoredRepositoryRemovedHandler(
    DbContext db,
    ILogger<MonitoredRepositoryRemovedHandler> logger) : IIntegrationEventHandler<MonitoredRepositoryRemoved>
{
    public async Task HandleAsync(MonitoredRepositoryRemoved @event, CancellationToken cancellationToken)
    {
        List<Issue> issues = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .Where(i => !(i is CompletedIssue))
            .ToListAsync(cancellationToken);

        foreach (Issue issue in issues.Where(HoldsLiveWorker))
        {
            logger.LogInformation(
                "Repository {RepositoryId} was removed but issue {IssueNumber} is in state {State}; preserving record.",
                @event.RepositoryId,
                issue.IssueNumber,
                issue.GetType().Name);
        }

        List<Issue> deletable = [.. issues.Where(issue => !HoldsLiveWorker(issue))];
        if (deletable.Count == 0)
        {
            return;
        }

        db.Set<Issue>().RemoveRange(deletable);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool HoldsLiveWorker(Issue issue) =>
        issue is TriageInProgressIssue or InProgressIssue or RevisionInProgressIssue or ReviewInProgressIssue;
}
