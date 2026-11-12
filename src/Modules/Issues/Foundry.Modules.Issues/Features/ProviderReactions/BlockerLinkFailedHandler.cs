using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

[IntegrationEventHandlerIdentity("Issues.BlockerLinkFailed")]
internal sealed class BlockerLinkFailedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<BlockerLinkFailedHandler> logger) : IIntegrationEventHandler<BlockerLinkFailed>
{
    public async Task HandleAsync(BlockerLinkFailed @event, CancellationToken cancellationToken)
    {
        Issue? issue = await db.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == @event.MonitoredRepositoryId)
            .FirstOrDefaultAsync(i => i.IssueNumber == @event.IssueNumber, cancellationToken);

        if (issue is not TriageBlockedIssue blocked)
        {
            logger.LogInformation(
                "BlockerLinkFailed received for repository {RepositoryId} issue {IssueNumber} but it is not a TriageBlockedIssue (state: {State}); ignoring.",
                @event.MonitoredRepositoryId,
                @event.IssueNumber,
                issue?.GetType().Name ?? "not found");
            return;
        }

        TriageFailedIssue failed = blocked.FailBlockerLink(
            new WorkerRunId(@event.WorkerRunId),
            @event.BlockerNumber,
            @event.ProviderMessage,
            @event.FailedAt);
        await db.TransitionAsync(blocked, failed, domainEventDispatcher, cancellationToken);
    }
}
