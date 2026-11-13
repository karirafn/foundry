using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ProviderIssueClosed")]
internal sealed class ProviderIssueClosedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ProviderIssueClosedHandler> logger) : IIntegrationEventHandler<ProviderIssueClosed>
{
    public async Task HandleAsync(ProviderIssueClosed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        switch (issue)
        {
            case ReviewIssue reviewIssue:
            {
                CompletedIssue completed = reviewIssue.Complete(now);
                await db.TransitionAsync(reviewIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            case ChecksPendingIssue checksPendingIssue:
            {
                CompletedIssue completed = checksPendingIssue.Complete(now);
                await db.TransitionAsync(checksPendingIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            case RebasePendingIssue rebasePendingIssue:
            {
                CompletedIssue completed = rebasePendingIssue.Complete(now);
                await db.TransitionAsync(rebasePendingIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            case AwaitingInputIssue awaitingInputIssue:
            {
                CompletedIssue completed = awaitingInputIssue.Complete(now);
                await db.TransitionAsync(awaitingInputIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            case PublishBlockedIssue publishBlockedIssue:
            {
                CompletedIssue completed = publishBlockedIssue.Complete(now);
                await db.TransitionAsync(publishBlockedIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            case PublishFailedIssue publishFailedIssue:
            {
                CompletedIssue completed = publishFailedIssue.Complete(now);
                await db.TransitionAsync(publishFailedIssue, completed, domainEventDispatcher, cancellationToken);
                break;
            }

            default:
                logger.LogWarning(
                    "ProviderIssueClosed received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue, ChecksPendingIssue, RebasePendingIssue, AwaitingInputIssue, PublishBlockedIssue or PublishFailedIssue (state: {State}); ignoring.",
                    @event.RepositoryId,
                    @event.IssueNumber,
                    issue?.GetType().Name ?? "not found");
                break;
        }
    }
}
