using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Reviews;

/// <summary>
/// Posts a completed review's verdict as one comment on its pull request (ADR 0113). The publishing command saves the
/// comment's ID in the posted-comment registry, so the review pass never reads it back as feedback. A failed post is
/// logged and recorded on the run, and nothing retries it: the issue routes on the verdict alone.
/// </summary>
[IntegrationEventHandlerIdentity("Workers.ReviewCommentPoster")]
internal sealed class ReviewCommentPoster(
    DbContext dbContext,
    IRunPublishingCommands runPublishingCommands,
    ILogger<ReviewCommentPoster> logger) : IIntegrationEventHandler<ReviewRunCompleted>
{
    public async Task HandleAsync(ReviewRunCompleted @event, CancellationToken cancellationToken)
    {
        WorkerRun? run = await dbContext.Set<WorkerRun>()
            .FirstOrDefaultAsync(r => r.Id == @event.WorkerRunId, cancellationToken);

        if (run is not CompletedRun { ReviewVerdict: { } verdict, SuppliedReviewFacts: { } facts } completed)
        {
            logger.LogWarning(
                "Worker run {WorkerRunId} is not a completed review ({RunState}); no review comment is posted.",
                @event.WorkerRunId,
                run?.GetType().Name ?? "not found");
            return;
        }

        if (completed.PreparedRepositoryId is not { } repositoryId)
        {
            await RecordFailureAsync(completed, "The run recorded no repository to post the review comment to.", cancellationToken);
            return;
        }

        Result posted = await runPublishingCommands.PostRunSummaryAsync(
            repositoryId,
            facts.PullRequestUrl.Value,
            ReviewVerdictComment.Compose(verdict),
            cancellationToken);

        if (posted is Result.Failure failure)
        {
            await RecordFailureAsync(completed, $"{failure.Error.Code}: {failure.Error.Message}", cancellationToken);
            return;
        }

        logger.LogInformation(
            "Worker run {WorkerRunId} posted its review verdict on {PullRequestUrl}.",
            completed.Id,
            facts.PullRequestUrl.Value);
    }

    private async Task RecordFailureAsync(CompletedRun run, string reason, CancellationToken cancellationToken)
    {
        run.RecordReviewCommentFailure(reason);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Worker run {WorkerRunId} could not post its review comment: {Reason}. The review's routing is unaffected.",
            run.Id,
            reason);
    }
}
