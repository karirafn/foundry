using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class ProviderPullRequestClosedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ProviderPullRequestClosedHandler> logger) : IIntegrationEventHandler<ProviderPullRequestClosed>
{
    private const string FailureReason = "Pull request closed without merge";

    public async Task HandleAsync(ProviderPullRequestClosed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ReviewIssue reviewIssue)
        {
            logger.LogWarning(
                "ProviderPullRequestClosed received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        FailedIssue failed = reviewIssue.Fail(FailureReason, DateTimeOffset.UtcNow);
        await db.TransitionAsync(reviewIssue, failed, domainEventDispatcher, cancellationToken);
    }
}
