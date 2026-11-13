using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ProviderPullRequestHeadMoved")]
internal sealed class ProviderPullRequestHeadMovedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<ProviderPullRequestHeadMovedHandler> logger) : IIntegrationEventHandler<ProviderPullRequestHeadMoved>
{
    public async Task HandleAsync(ProviderPullRequestHeadMoved @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ReviewIssue reviewIssue)
        {
            // Expected when the issue already moved on (feedback, close) before this event was delivered.
            logger.LogInformation(
                "ProviderPullRequestHeadMoved received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        ChecksPendingIssue checksPending = reviewIssue.RecheckAfterHeadMoved(timeProvider.GetUtcNow());
        await db.TransitionAsync(reviewIssue, checksPending, domainEventDispatcher, cancellationToken);
    }
}
