using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.ProviderReactions;

/// <summary>
/// Fails triage when the provider refused to link a blocker triage named. The event is delivered after the fact, so
/// it is acted on only while the issue still waits on the blockers that run named; an issue that has since been
/// triaged again, or that waits on blockers found before triage, no longer has a link to refuse and stays as it is.
/// </summary>
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

        WorkerRunId workerRunId = WorkerRunId.From(@event.WorkerRunId);

        if (!blocked.WasLinkRefusedForTriageRun(workerRunId, @event.BlockerNumber))
        {
            logger.LogInformation(
                "BlockerLinkFailed from run {WorkerRunId} for repository {RepositoryId} issue {IssueNumber} does not describe its blocked state; ignoring.",
                workerRunId.Value,
                @event.MonitoredRepositoryId,
                @event.IssueNumber);
            return;
        }

        TriageFailedIssue failed = blocked.FailBlockerLink(
            workerRunId,
            @event.BlockerNumber,
            @event.ProviderMessage,
            @event.FailedAt);
        await db.TransitionAsync(blocked, failed, domainEventDispatcher, cancellationToken);
    }
}
