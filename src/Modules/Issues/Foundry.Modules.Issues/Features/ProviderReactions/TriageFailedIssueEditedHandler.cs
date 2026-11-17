using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

/// <summary>
/// Sends a failed triage back to triage when its issue description was edited after the failure (issue #773,
/// ADR 0097). An issue that already left <c>triage_failed</c> (an operator pressed Retry triage or Skip triage first,
/// or the event is redelivered) is left as it is.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.TriageFailedIssueEdited")]
internal sealed class TriageFailedIssueEditedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<TriageFailedIssueEditedHandler> logger) : IIntegrationEventHandler<TriageFailedIssueEdited>
{
    public async Task HandleAsync(TriageFailedIssueEdited @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not TriageFailedIssue failed || !failed.AcceptsEditSeenAt(@event.EditSeenAt))
        {
            logger.LogInformation(
                "TriageFailedIssueEdited received for repository {RepositoryId} issue {IssueNumber} but it is not triage-failed since before the edit (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        TriageQueuedIssue queued = failed.RetryTriage(timeProvider.GetUtcNow());
        await db.TransitionAsync(failed, queued, domainEventDispatcher, cancellationToken);

        logger.LogInformation(
            "Issue #{IssueNumber} in repository {RepositoryId} was edited after its triage failed; queued for triage again.",
            @event.IssueNumber,
            @event.RepositoryId);
    }
}
