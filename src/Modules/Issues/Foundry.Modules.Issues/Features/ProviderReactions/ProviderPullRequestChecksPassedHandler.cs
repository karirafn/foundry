using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ProviderPullRequestChecksPassed")]
internal sealed class ProviderPullRequestChecksPassedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<ProviderPullRequestChecksPassedHandler> logger) : IIntegrationEventHandler<ProviderPullRequestChecksPassed>
{
    public async Task HandleAsync(ProviderPullRequestChecksPassed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ReviewIssue reviewIssue)
        {
            // Expected when the issue already moved on (feedback, close, an earlier delivery) before this event arrived.
            logger.LogInformation(
                "ProviderPullRequestChecksPassed received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        if (reviewIssue.RecheckAfterChecksPassed(timeProvider.GetUtcNow()) is not { } checksPending)
        {
            // The issue already holds a passed verdict, e.g. a redelivery after the gate resolved it.
            logger.LogInformation(
                "ProviderPullRequestChecksPassed received for repository {RepositoryId} issue {IssueNumber} but its checks verdict already passed; ignoring.",
                @event.RepositoryId,
                @event.IssueNumber);
            return;
        }

        await db.TransitionAsync(reviewIssue, checksPending, domainEventDispatcher, cancellationToken);
    }
}
