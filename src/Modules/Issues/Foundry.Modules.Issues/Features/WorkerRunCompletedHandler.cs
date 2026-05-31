using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Issues.Features;

internal sealed class WorkerRunCompletedHandler(
    DbContext db,
    ILogger<WorkerRunCompletedHandler> logger) : IIntegrationEventHandler<WorkerRunCompleted>
{
    public async Task HandleAsync(WorkerRunCompleted @event, CancellationToken cancellationToken)
    {
        IssueId issueId = IssueId.From(@event.IssueId);

        Issue? issue = await db.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);

        if (issue is InProgressIssue inProgress)
        {
            if (@event.BranchName is not null && @event.PullRequestUrl is not null)
            {
                ReviewIssue review = inProgress.MarkInReview(
                    @event.WorkerRunId,
                    @event.BranchName,
                    @event.PullRequestUrl,
                    DateTimeOffset.UtcNow);
                await db.TransitionAsync(inProgress, review, cancellationToken);
            }
            else
            {
                UnchangedIssue unchanged = inProgress.MarkUnchanged(@event.WorkerRunId);
                await db.TransitionAsync(inProgress, unchanged, cancellationToken);
            }

            return;
        }

        if (issue is RevisionInProgressIssue revisionInProgress)
        {
            if (@event.BranchName is not null && @event.PullRequestUrl is not null)
            {
                ReviewIssue review = revisionInProgress.MarkInReview(DateTimeOffset.UtcNow);
                await db.TransitionAsync(revisionInProgress, review, cancellationToken);
            }
            else
            {
                ReviewIssue review = revisionInProgress.MarkUnchanged(DateTimeOffset.UtcNow);
                await db.TransitionAsync(revisionInProgress, review, cancellationToken);
            }

            return;
        }

        logger.LogWarning(
            "WorkerRunCompleted received for issue {IssueId} but it is not InProgress (state: {State}); ignoring.",
            @event.IssueId,
            issue?.GetType().Name ?? "not found");
    }
}
