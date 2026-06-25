using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class ProviderIssueUntrackedHandler(
    DbContext db,
    ILogger<ProviderIssueUntrackedHandler> logger) : IIntegrationEventHandler<ProviderIssueUntracked>
{
    public async Task HandleAsync(ProviderIssueUntracked @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null)
        {
            logger.LogWarning(
                "ProviderIssueUntracked received for repository {RepositoryId} issue {IssueNumber} but it was not found; ignoring.",
                @event.RepositoryId,
                @event.IssueNumber);
            return;
        }

        bool isRestingState = issue is DetectedIssue
            or QueuedIssue
            or BlockedIssue
            or FailedIssue
            or ContinuableFailedIssue
            or RevisionFailedIssue
            or RevisionQueuedIssue
            or ContinuationQueuedIssue;

        if (!isRestingState)
        {
            logger.LogInformation(
                "ProviderIssueUntracked received for repository {RepositoryId} issue {IssueNumber} in state {State}; preserving record.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue.GetType().Name);
            return;
        }

        db.Set<Issue>().Remove(issue);
        await db.SaveChangesAsync(cancellationToken);
    }
}
