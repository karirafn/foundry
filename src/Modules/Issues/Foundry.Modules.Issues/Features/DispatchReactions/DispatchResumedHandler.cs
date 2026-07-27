using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Events;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.DispatchReactions;

internal sealed class DispatchResumedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<DispatchResumedHandler> logger) : IIntegrationEventHandler<DispatchResumed>
{
    public async Task HandleAsync(DispatchResumed @event, CancellationToken cancellationToken)
    {
        List<FailedIssue> failedIssues = await db.Set<FailedIssue>()
            .Where(i => EF.Functions.Like(i.FailureReason, WorkerRunFailed.UsageLimitedReason + "%"))
            .ToListAsync(cancellationToken);

        List<ContinuableFailedIssue> continuableFailedIssues = await db.Set<ContinuableFailedIssue>()
            .Where(i => EF.Functions.Like(i.FailureReason, WorkerRunFailed.UsageLimitedReason + "%"))
            .ToListAsync(cancellationToken);

        foreach (FailedIssue failed in failedIssues)
        {
            QueuedIssue queued = failed.Retry();
            await db.TransitionAsync(failed, queued, domainEventDispatcher, cancellationToken);

            logger.LogInformation(
                "Dispatch resumed: re-queued issue #{IssueNumber} (reason: {FailureReason}, was FailedIssue).",
                failed.IssueNumber,
                failed.FailureReason);
        }

        foreach (ContinuableFailedIssue continuableFailed in continuableFailedIssues)
        {
            ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
            await db.TransitionAsync(continuableFailed, continuationQueued, domainEventDispatcher, cancellationToken);

            logger.LogInformation(
                "Dispatch resumed: re-queued issue #{IssueNumber} (reason: {FailureReason}, was ContinuableFailedIssue).",
                continuableFailed.IssueNumber,
                continuableFailed.FailureReason);
        }
    }
}
