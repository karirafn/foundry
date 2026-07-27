using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

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

        if (issue is not ReviewIssue reviewIssue)
        {
            logger.LogWarning(
                "ProviderIssueClosed received for repository {RepositoryId} issue {IssueNumber} but it is not a ReviewIssue (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        CompletedIssue completed = reviewIssue.Complete(DateTimeOffset.UtcNow);
        await db.TransitionAsync(reviewIssue, completed, domainEventDispatcher, cancellationToken);
    }
}
