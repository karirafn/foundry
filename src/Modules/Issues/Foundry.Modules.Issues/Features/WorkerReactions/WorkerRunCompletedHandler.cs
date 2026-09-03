using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

internal sealed class WorkerRunCompletedHandler(
    DbContext db,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<WorkerRunCompletedHandler> logger) : IIntegrationEventHandler<WorkerRunCompleted>
{
    public async Task HandleAsync(WorkerRunCompleted @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is InProgressIssue inProgress)
        {
            if (@event.WorkerRunId != inProgress.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunCompleted for issue {IssueId} has run id {EventRunId} which does not match current run id {CurrentRunId}; ignoring stale event.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    inProgress.WorkerRunId);
                return;
            }

            switch (@event.MergeState)
            {
                case WorkerRunMergeState.Merged:
                    if (@event.BranchName is null || @event.PullRequestUrl is null)
                    {
                        logger.LogError(
                            "WorkerRunCompleted for issue {IssueId} has MergeState=Merged but BranchName or PullRequestUrl is null; skipping transition.",
                            @event.IssueId);
                        return;
                    }

                    CompletedIssue completed = inProgress.MarkCompleted(
                        @event.BranchName,
                        @event.PullRequestUrl,
                        DateTimeOffset.UtcNow);
                    await db.TransitionAsync(inProgress, completed, domainEventDispatcher, cancellationToken);
                    break;

                case WorkerRunMergeState.Open:
                    if (@event.BranchName is null || @event.PullRequestUrl is null)
                    {
                        logger.LogError(
                            "WorkerRunCompleted for issue {IssueId} has MergeState=Open but BranchName or PullRequestUrl is null; skipping transition.",
                            @event.IssueId);
                        return;
                    }

                    ReviewIssue review = inProgress.MarkInReview(
                        @event.BranchName,
                        @event.PullRequestUrl,
                        DateTimeOffset.UtcNow);
                    await db.TransitionAsync(inProgress, review, domainEventDispatcher, cancellationToken);
                    break;

                default:
                    UnchangedIssue unchanged = inProgress.MarkUnchanged();
                    await db.TransitionAsync(inProgress, unchanged, domainEventDispatcher, cancellationToken);
                    break;
            }

            return;
        }

        if (issue is RevisionInProgressIssue revisionInProgress)
        {
            if (@event.WorkerRunId != revisionInProgress.WorkerRunId)
            {
                logger.LogWarning(
                    "WorkerRunCompleted for issue {IssueId} has run id {EventRunId} which does not match current run id {CurrentRunId}; ignoring stale event.",
                    @event.IssueId,
                    @event.WorkerRunId,
                    revisionInProgress.WorkerRunId);
                return;
            }

            if (@event.BranchName is not null && @event.PullRequestUrl is not null)
            {
                ReviewIssue review = revisionInProgress.MarkInReview(DateTimeOffset.UtcNow);
                await db.TransitionAsync(revisionInProgress, review, domainEventDispatcher, cancellationToken);
            }
            else
            {
                ReviewIssue review = revisionInProgress.MarkUnchanged(DateTimeOffset.UtcNow);
                await db.TransitionAsync(revisionInProgress, review, domainEventDispatcher, cancellationToken);
            }

            return;
        }

        logger.LogWarning(
            "WorkerRunCompleted received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
