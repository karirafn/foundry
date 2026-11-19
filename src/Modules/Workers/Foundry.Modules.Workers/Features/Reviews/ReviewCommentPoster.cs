using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Reviews;

/// <summary>
/// Posts a completed review's findings on their pull request (ADR 0113): each finding on a line the reviewed diff adds
/// goes inline first, then the verdict follows as one comment listing the rest. The publishing commands save every
/// posted ID in the posted-comment registry, so the review pass never reads them back as feedback. A failed inline post
/// leaves its findings in the comment, and every failure is logged and recorded on the run; nothing retries it, and the
/// issue routes on the verdict alone.
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

        (IReadOnlyCollection<ReviewFinding> postedInline, IReadOnlyList<string> inlineFailures) =
            await PostInlineAsync(repositoryId, verdict, facts, cancellationToken);

        Result posted = await runPublishingCommands.PostRunSummaryAsync(
            repositoryId,
            facts.PullRequestUrl.Value,
            ReviewVerdictComment.Compose(verdict, postedInline),
            cancellationToken);

        List<string> failures = [.. inlineFailures.Select(reason => $"Inline: {reason}")];
        if (posted is Result.Failure failure)
        {
            failures.Insert(0, $"{failure.Error.Code}: {failure.Error.Message}");
        }

        if (failures.Count > 0)
        {
            await RecordFailureAsync(completed, string.Join("; ", failures), cancellationToken);
            return;
        }

        logger.LogInformation(
            "Worker run {WorkerRunId} posted its review verdict on {PullRequestUrl}.",
            completed.Id,
            facts.PullRequestUrl.Value);
    }

    /// <summary>
    /// Posts every finding whose line the reviewed diff adds; any other line may not be in the provider's diff. Returns
    /// the findings posted and why the others were not, so those stay in the summary comment.
    /// </summary>
    private async Task<(IReadOnlyCollection<ReviewFinding> Posted, IReadOnlyList<string> Failures)> PostInlineAsync(
        MonitoredRepositoryId repositoryId,
        RecordedReviewVerdict verdict,
        SuppliedReviewFacts facts,
        CancellationToken cancellationToken)
    {
        // The review a carried approval came from already posted these findings.
        if (verdict.CarriedFrom is not null)
        {
            return ([], []);
        }

        ILookup<InlineReviewComment, ReviewFinding> inline = verdict.Findings
            .Where(finding => finding.Line is { } line && facts.IsAddedLine(finding.Path, line))
            .ToLookup(finding => new InlineReviewComment(finding.Path, finding.Line!.Value, ReviewVerdictComment.InlineBody(finding)));

        if (inline.Count == 0)
        {
            return ([], []);
        }

        Result<InlineReviewResult> result = await runPublishingCommands.PostInlineReviewAsync(
            repositoryId,
            facts.PullRequestUrl.Value,
            facts.ReviewedCommit.Value,
            ReviewVerdictComment.InlineReviewSummary(verdict, inline.Sum(group => group.Count())),
            [.. inline.Select(group => group.Key)],
            cancellationToken);

        return result switch
        {
            Result<InlineReviewResult>.Success { Value: var outcome } =>
                ([.. outcome.Posted.SelectMany(comment => inline[comment])], outcome.Failures),
            Result<InlineReviewResult>.Failure { Error: var error } => ([], [$"{error.Code}: {error.Message}"]),
            _ => throw new UnreachableException(),
        };
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
