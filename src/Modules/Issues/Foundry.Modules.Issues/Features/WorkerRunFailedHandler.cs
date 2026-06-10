using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerRunFailedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<WorkerRunFailedHandler> logger) : IIntegrationEventHandler<WorkerRunFailed>
{
    public async Task HandleAsync(WorkerRunFailed @event, CancellationToken cancellationToken)
    {
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is InProgressIssue inProgress)
        {
            if (@event.BranchName is not null)
            {
                const int MaxLatestProgressLength = 2000;
                string latestProgress = (@event.LatestProgress ?? string.Empty).Length > MaxLatestProgressLength
                    ? (@event.LatestProgress ?? string.Empty)[..MaxLatestProgressLength]
                    : @event.LatestProgress ?? string.Empty;
                ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
                    @event.WorkerRunId,
                    @event.BranchName,
                    latestProgress,
                    @event.ReasonDescription,
                    failedAt);
                await db.TransitionAsync(inProgress, continuableFailed, domainEventDispatcher, cancellationToken);
            }
            else
            {
                FailedIssue failed = inProgress.MarkFailed(
                    @event.WorkerRunId,
                    @event.ReasonDescription,
                    failedAt);
                await db.TransitionAsync(inProgress, failed, domainEventDispatcher, cancellationToken);
            }
            return;
        }

        // Revision failures always become RevisionFailedIssue regardless of branch presence —
        // the revision path has its own retry mechanism (RevisionFailedIssue.Retry → RevisionQueuedIssue).
        if (issue is RevisionInProgressIssue revisionInProgress)
        {
            RevisionFailedIssue revisionFailed = revisionInProgress.MarkFailed(
                @event.WorkerRunId,
                @event.ReasonDescription,
                failedAt);
            await db.TransitionAsync(revisionInProgress, revisionFailed, domainEventDispatcher, cancellationToken);
            return;
        }

        logger.LogWarning(
            "WorkerRunFailed received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
