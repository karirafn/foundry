using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.CredentialReactions;

internal sealed class CreditsRestoredHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<CreditsRestoredHandler> logger) : IIntegrationEventHandler<CreditsRestored>
{
    public async Task HandleAsync(CreditsRestored @event, CancellationToken cancellationToken)
    {
        List<FailedIssue> failedIssues = await db.Set<FailedIssue>()
            .Where(i => i.FailureReason == WorkerRunFailed.CreditsExhaustedReason)
            .ToListAsync(cancellationToken);

        List<ContinuableFailedIssue> continuableFailedIssues = await db.Set<ContinuableFailedIssue>()
            .Where(i => i.FailureReason == WorkerRunFailed.CreditsExhaustedReason)
            .ToListAsync(cancellationToken);

        foreach (FailedIssue failed in failedIssues)
        {
            QueuedIssue queued = failed.Retry();
            await db.TransitionAsync(failed, queued, domainEventDispatcher, cancellationToken);

            logger.LogInformation(
                "Credits restored: re-queued issue #{IssueNumber} (was FailedIssue, credits-exhausted).",
                failed.IssueNumber);
        }

        foreach (ContinuableFailedIssue continuableFailed in continuableFailedIssues)
        {
            ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
            await db.TransitionAsync(
                continuableFailed,
                continuationQueued,
                domainEventDispatcher,
                cancellationToken);

            logger.LogInformation(
                "Credits restored: re-queued issue #{IssueNumber} (was ContinuableFailedIssue, credits-exhausted).",
                continuableFailed.IssueNumber);
        }
    }
}
