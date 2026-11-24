using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.ChecksRetried")]
internal sealed class ChecksRetriedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<ChecksRetriedHandler> logger) : IIntegrationEventHandler<ChecksRetried>
{
    public async Task HandleAsync(ChecksRetried @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.RepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not ChecksPendingIssue checksPending || !checksPending.OwesChecksRetryFor(@event.HeadSha))
        {
            logger.LogWarning(
                "ChecksRetried received for repository {RepositoryId} issue {IssueNumber} but it owes no checks retry for that head (state: {State}); ignoring.",
                @event.RepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        ChecksPendingIssue retried = checksPending.RecordChecksRetried(@event.HeadSha, @event.RetriedAt);
        await db.TransitionAsync(checksPending, retried, domainEventDispatcher, cancellationToken);
    }
}
