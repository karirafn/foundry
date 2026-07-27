using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

internal sealed class ProviderPullRequestClosedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ProviderPullRequestClosedHandler> logger) : IIntegrationEventHandler<ProviderPullRequestClosed>
{
    private const string FailureReason = "Pull request closed without merge";
    private const string FailureCategory = "pr_closed";

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

        ContinuableFailedIssue failed = reviewIssue.Fail(FailureReason, FailureCategory, DateTimeOffset.UtcNow);
        await db.TransitionAsync(reviewIssue, failed, domainEventDispatcher, cancellationToken);
    }
}
