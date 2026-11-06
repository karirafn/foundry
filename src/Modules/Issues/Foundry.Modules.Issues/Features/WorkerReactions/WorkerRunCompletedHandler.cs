using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features.WorkerReactions;

[IntegrationEventHandlerIdentity("Issues.WorkerRunCompleted")]
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

                    // RunStartedAt is always set in normal operation (from ActiveRun.StartedAt, which is non-nullable).
                    // The ?? UtcNow fallback exists only for back-compat with WorkerRunCompleted events that were
                    // serialized before this field was added and are drained across the deploy boundary, reproducing
                    // the old completion-time cutoff for exactly that bounded window.
                    // An open PR is rebased onto the default branch before the checks gate (ADR 0087).
                    RebasePendingIssue rebasePending = inProgress.MarkRebasePending(
                        @event.BranchName,
                        @event.PullRequestUrl,
                        @event.RunStartedAt ?? DateTimeOffset.UtcNow);
                    await db.TransitionAsync(inProgress, rebasePending, domainEventDispatcher, cancellationToken);
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

            // A ReviewFeedback revision anchors the cutoff to the newest comment it consumed, so feedback posted
            // after the consumed batch is not dropped. A revision that consumed no comments (FailedChecks,
            // RebaseConflict, or ReviewFeedback without a consumed timestamp) anchors to the run's start time, the
            // same as a first run, so a comment posted while the run was working is still read.
            // The ?? UtcNow fallback is back-compat for events serialized before RunStartedAt existed (see above).
            // Both paths rebase: a revision that pushed nothing may still be behind the default branch (ADR 0087).
            DateTimeOffset revisionRunStartedAt = @event.RunStartedAt ?? DateTimeOffset.UtcNow;
            if (@event.BranchName is not null && @event.PullRequestUrl is not null)
            {
                RebasePendingIssue revisionRebasePending = revisionInProgress.MarkRebasePending(revisionRunStartedAt);
                await db.TransitionAsync(revisionInProgress, revisionRebasePending, domainEventDispatcher, cancellationToken);
            }
            else
            {
                RebasePendingIssue revisionRebasePending = revisionInProgress.MarkUnchangedRebasePending(revisionRunStartedAt);
                await db.TransitionAsync(revisionInProgress, revisionRebasePending, domainEventDispatcher, cancellationToken);
            }

            return;
        }

        logger.LogWarning(
            "WorkerRunCompleted received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
