using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

/// <summary>
/// Sends an issue with open work back to triage when its spec was rewritten since its last triage (ADR 0125), holding
/// its branch and pull request so a ready verdict resumes them. An issue no longer in a watched state (a run started,
/// or the event is redelivered after the move) is left as it is.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.IssueSpecRewritten")]
internal sealed class IssueSpecRewrittenHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<IssueSpecRewrittenHandler> logger) : IIntegrationEventHandler<IssueSpecRewritten>
{
    public async Task HandleAsync(IssueSpecRewritten @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is null || Retriage(issue, timeProvider.GetUtcNow()) is not { } queued)
        {
            logger.LogInformation(
                "IssueSpecRewritten received for repository {RepositoryId} issue {IssueNumber} but its spec is not watched in its state ({State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        await db.TransitionAsync(issue, queued, domainEventDispatcher, cancellationToken);

        logger.LogInformation(
            "The spec of issue #{IssueNumber} in repository {RepositoryId} was rewritten since its last triage; queued for triage holding its open work.",
            @event.IssueNumber,
            @event.RepositoryId);
    }

    private static TriageQueuedIssue? Retriage(Issue issue, DateTimeOffset now) =>
        issue switch
        {
            RevisionQueuedIssue revisionQueued => revisionQueued.RetriageRewrittenSpec(now),
            RevisionBlockedIssue revisionBlocked => revisionBlocked.RetriageRewrittenSpec(now),
            ContinuationQueuedIssue continuationQueued => continuationQueued.RetriageRewrittenSpec(now),
            ContinuationBlockedIssue continuationBlocked => continuationBlocked.RetriageRewrittenSpec(now),
            ReviewIssue review => review.RetriageRewrittenSpec(now),
            RevisionFailedIssue revisionFailed => revisionFailed.RetriageRewrittenSpec(now),
            _ => null,
        };
}
