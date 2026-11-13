using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

/// <summary>
/// An operator retried a run's exhausted publish (ADR 0100): the same run publishes again, so its issue returns to
/// work in progress and later completes or fails through the run's own events.
/// </summary>
[IntegrationEventHandlerIdentity("Issues.WorkerRunPublishRetryRequested")]
internal sealed class WorkerRunPublishRetryRequestedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    TimeProvider timeProvider,
    ILogger<WorkerRunPublishRetryRequestedHandler> logger) : IIntegrationEventHandler<WorkerRunPublishRetryRequested>
{
    public async Task HandleAsync(WorkerRunPublishRetryRequested @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is not PublishFailedIssue publishFailed || publishFailed.WorkerRunId != @event.WorkerRunId)
        {
            logger.LogWarning(
                "WorkerRunPublishRetryRequested for issue {IssueId} run {EventRunId} does not match an issue waiting to publish that run (state: {State}); ignoring.",
                @event.IssueId,
                @event.WorkerRunId,
                issue?.GetType().Name ?? "not found");
            return;
        }

        Issue next = publishFailed.RetryPublish(timeProvider.GetUtcNow());
        await db.TransitionAsync(publishFailed, next, domainEventDispatcher, cancellationToken);
    }
}
