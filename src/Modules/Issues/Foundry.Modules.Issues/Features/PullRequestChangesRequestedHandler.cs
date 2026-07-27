using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class PullRequestChangesRequestedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<PullRequestChangesRequestedHandler> logger) : IIntegrationEventHandler<PullRequestChangesRequested>
{
    public async Task HandleAsync(PullRequestChangesRequested @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ReviewIssue reviewIssue)
        {
            logger.LogWarning(
                "PullRequestChangesRequested received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        RevisionQueuedIssue revisionQueued = reviewIssue.Revise(@event.Comments);
        await db.TransitionAsync(reviewIssue, revisionQueued, domainEventDispatcher, cancellationToken);
    }
}
