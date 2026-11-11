using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AwaitingInput;

/// <summary>
/// The awaiting-input step (ADR 0096): for every awaiting-input issue it first applies the review pass's ways out (the
/// issue closed, or its pull request closed without merging), then posts the questions while they are unposted, or
/// reads the question's thread for input once they are posted. It runs on every scheduled poll and on
/// <see cref="NudgePass.AwaitingInput"/> nudges.
/// </summary>
internal sealed class AwaitingInputStep(
    IIssueQueries issueQueries,
    IAwaitingInputCommands awaitingInputCommands,
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    PostedCommentRegistry postedCommentRegistry,
    ActionableFeedbackPolicy feedbackPolicy,
    WebhookNudgeQueue nudgeQueue,
    ILogger<AwaitingInputStep> logger)
{
    /// <summary>
    /// Runs the step for every awaiting-input issue of <paramref name="repository"/>, or only
    /// <paramref name="issueNumber"/> when supplied (nudged).
    /// </summary>
    public async Task RunAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<AwaitingInputIssueInfo> waiting = [.. (await issueQueries.GetAwaitingInputIssuesAsync(repository.Id, cancellationToken))
            .Where(info => issueNumber is null || info.IssueNumber == issueNumber)];

        if (waiting.Count == 0)
        {
            return;
        }

        PostedCommentIds postedComments = waiting.Any(info => info.PostState is AwaitingInputPostState.Posted)
            ? await postedCommentRegistry.LoadAsync(repository.Id, cancellationToken)
            : PostedCommentIds.None;

        List<IIntegrationEvent> events = [];

        // HTTP calls are sequential: awaiting-input issues per repository are expected to be small (design decision D6).
        foreach (AwaitingInputIssueInfo info in waiting)
        {
            if (await FindWayOutAsync(repository, provider, info, cancellationToken) is { } wayOut)
            {
                events.Add(wayOut);
                continue;
            }

            switch (info.PostState)
            {
                case AwaitingInputPostState.Unposted { DueAt: DateTimeOffset dueAt } when dueAt <= now:
                    await PostQuestionsAsync(repository, provider, info, now, cancellationToken);
                    break;

                case AwaitingInputPostState.Posted posted:
                    if (await ReadInputAsync(repository, provider, info, posted.PostedAt, postedComments, cancellationToken) is { } arrived)
                    {
                        events.Add(arrived);
                    }

                    break;
            }
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The review pass's ways out: a closed issue completes; a pull request closed without merging fails continuably;
    /// a merged pull request waits for its issue to close. Returns <see langword="null"/> while the issue keeps waiting
    /// or a read failed (retried on the next run).
    /// </summary>
    private static async Task<IIntegrationEvent?> FindWayOutAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        CancellationToken cancellationToken)
    {
        Result<bool> isClosedResult = await provider.IsIssueClosedAsync(repository.Slug, info.IssueNumber, cancellationToken);

        if (isClosedResult is Result<bool>.Success { Value: true })
        {
            return new ProviderIssueClosed(repository.Id, info.IssueNumber);
        }

        if (info.PullRequestUrl is not { } pullRequestUrl || isClosedResult is not Result<bool>.Success)
        {
            return null;
        }

        Result<PullRequestStatus> prStatusResult = await provider.GetPullRequestStatusAsync(
            repository.Slug,
            pullRequestUrl,
            cancellationToken);

        return prStatusResult is Result<PullRequestStatus>.Success { Value: { IsClosed: true, IsMerged: false } }
            ? new ProviderPullRequestClosed(repository.Id, info.IssueNumber)
            : null;
    }

    /// <summary>
    /// Posts the questions on the pull request when there is one, otherwise on the issue. A posted question is saved at
    /// once, together with its posted-comment registry entry, so a later failure in this run never posts it twice.
    /// </summary>
    private async Task PostQuestionsAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CommentThread thread = info.PullRequestUrl is { } pullRequestUrl
            ? new CommentThread.PullRequest(pullRequestUrl)
            : new CommentThread.Issue(info.IssueNumber);

        Result<ProviderCommentId> result = await PostSafelyAsync(
            repository,
            provider,
            thread,
            QuestionComment.Compose(info.Questions),
            cancellationToken);

        switch (result)
        {
            case Result<ProviderCommentId>.Success success:
                postedCommentRegistry.Record(repository.Id, success.Value, now);
                await awaitingInputCommands.RecordQuestionPostedAsync(
                    info.IssueId,
                    success.Value.Value,
                    now,
                    QuestionComment.LinkTo(info.PullRequestUrl ?? info.IssueUrl, success.Value),
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Posted the questions of awaiting-input issue #{IssueNumber} in '{Slug}' as comment {CommentId}.",
                    info.IssueNumber,
                    repository.Slug,
                    success.Value);
                break;

            case Result<ProviderCommentId>.Failure failure:
                await awaitingInputCommands.RecordQuestionPostFailedAsync(
                    info.IssueId,
                    failure.Error.Message,
                    CommentPostErrors.IsTransient(failure.Error),
                    now,
                    cancellationToken);
                logger.LogWarning(
                    "Posting the questions of awaiting-input issue #{IssueNumber} in '{Slug}' failed: {Error}",
                    info.IssueNumber,
                    repository.Slug,
                    failure.Error.Message);
                break;
        }
    }

    private static async Task<Result<ProviderCommentId>> PostSafelyAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        CommentThread thread,
        string body,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.PostCommentAsync(repository.Slug, thread, body, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result<ProviderCommentId>.Fail(CommentPostErrors.Transient(ex.Message));
        }
    }

    /// <summary>
    /// Reads the question's thread after its post time and returns the input that arrived: the eligible comments (not a
    /// bot, not system, not in a resolved thread, not in the posted-comment registry) that outlasted the quiet period.
    /// Comments still inside the quiet period schedule a follow-up nudge.
    /// </summary>
    private async Task<AwaitingInputArrived?> ReadInputAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        DateTimeOffset postedAt,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        ReviewFeedback? feedback = info.PullRequestUrl is { } pullRequestUrl
            ? await provider.GetReviewFeedbackAsync(repository.Slug, pullRequestUrl, postedAt, postedComments, cancellationToken)
                is Result<ReviewFeedback>.Success { Value: var reviewFeedback } ? reviewFeedback : null
            : await provider.GetIssueFeedbackAsync(repository.Slug, info.IssueNumber, postedAt, postedComments, cancellationToken)
                is Result<IReadOnlyList<ProviderComment>>.Success { Value: var comments }
                ? feedbackPolicy.Apply(comments, postedAt, postedComments)
                : null;

        if (feedback is null)
        {
            return null;
        }

        if (feedback.HeldBackUntil is { } heldBackUntil)
        {
            nudgeQueue.TryEnqueue(new Nudge(
                new NudgeKey(repository.Id, NudgePass.AwaitingInput, info.IssueNumber),
                heldBackUntil));
        }

        if (feedback.Comments.Count == 0 || feedback.NewestCommentAt is not { } arrivedAt)
        {
            return null;
        }

        string input = string.Join("\n\n", feedback.Comments.Select(comment => comment.Body.Trim()));
        return new AwaitingInputArrived(repository.Id, info.IssueNumber, input, arrivedAt);
    }
}
