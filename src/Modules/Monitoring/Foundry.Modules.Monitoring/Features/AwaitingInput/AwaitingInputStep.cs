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
/// once they are posted reads the question's thread and, on an issue-thread wait, the issue body, and lets the issue's
/// <see cref="IssueWatch"/> decide whether the round's input has arrived. It runs on every scheduled poll and on
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

        Dictionary<int, IssueWatch> watches = await dbContext.Set<IssueWatch>()
            .Where(watch => watch.MonitoredRepositoryId == repository.Id)
            .ToDictionaryAsync(watch => watch.IssueNumber, cancellationToken);

        if (issueNumber is null)
        {
            ForgetWatchesOfIssuesNoLongerWaiting(watches, waiting);
        }

        if (waiting.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        PostedCommentIds postedComments = waiting.Any(info => info.PostState is AwaitingInputPostState.Posted)
            ? await postedCommentRegistry.LoadAsync(repository.Id, cancellationToken)
            : PostedCommentIds.None;

        List<IIntegrationEvent> events = [];

        // HTTP calls are sequential: awaiting-input issues per repository are expected to be small (design decision D6).
        foreach (AwaitingInputIssueInfo info in waiting)
        {
            IssueStatus? status = await provider.GetIssueStatusAsync(repository.Slug, info.IssueNumber, cancellationToken)
                is Result<IssueStatus>.Success { Value: var read } ? read : null;

            if (await FindWayOutAsync(repository, provider, info, status, cancellationToken) is { } wayOut)
            {
                events.Add(wayOut);
                continue;
            }

            switch (info.PostState)
            {
                case AwaitingInputPostState.Unposted { DueAt: DateTimeOffset dueAt } when dueAt <= now:
                    await PostQuestionsAsync(repository, provider, info, status, watches, now, cancellationToken);
                    break;

                case AwaitingInputPostState.Posted posted:
                    IssueWatch watch = WatchOf(repository, info, watches);
                    if (await ReadInputAsync(repository, provider, info, posted.PostedAt, postedComments, watch, status, now, cancellationToken) is { } arrived)
                    {
                        events.Add(arrived);
                        dbContext.Set<IssueWatch>().Remove(watch);
                        watches.Remove(info.IssueNumber);
                    }

                    break;
            }
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes the awaiting-input watches of issues that are no longer awaiting input (a full pass sees every waiting
    /// issue). A failed triage's watch belongs to the failed-triage pass, which removes it.
    /// </summary>
    private void ForgetWatchesOfIssuesNoLongerWaiting(
        Dictionary<int, IssueWatch> watches,
        IReadOnlyList<AwaitingInputIssueInfo> waiting)
    {
        HashSet<int> waitingNumbers = [.. waiting.Select(info => info.IssueNumber)];

        foreach (IssueWatch stale in watches.Values
            .Where(watch => watch.Wait is WatchedWait.AwaitingInput && !waitingNumbers.Contains(watch.IssueNumber))
            .ToList())
        {
            dbContext.Set<IssueWatch>().Remove(stale);
            watches.Remove(stale.IssueNumber);
        }
    }

    /// <summary>
    /// The issue's watch. A wait posted before watches existed has none: it starts one without a baseline, so the
    /// first successful body read becomes the baseline rather than a change. A watch left from a failed triage the
    /// failed-triage pass has not removed yet is replaced the same way: its baseline predates the question.
    /// </summary>
    private IssueWatch WatchOf(
        MonitoredRepository repository,
        AwaitingInputIssueInfo info,
        Dictionary<int, IssueWatch> watches)
    {
        if (watches.TryGetValue(info.IssueNumber, out IssueWatch? existing) && existing.Wait is WatchedWait.AwaitingInput)
        {
            return existing;
        }

        IssueWatch started = IssueWatch.Start(repository.Id, info.IssueNumber, baselineBodyHash: null);
        ReplaceWatch(watches, started);
        return started;
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
        IssueStatus? status,
        CancellationToken cancellationToken)
    {
        if (status is { IsClosed: true })
        {
            return new ProviderIssueClosed(repository.Id, info.IssueNumber);
        }

        if (info.PullRequestUrl is not { } pullRequestUrl || status is null)
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
    /// once, together with its posted-comment registry entry and a fresh <see cref="IssueWatch"/>, so a later failure in
    /// this run never posts it twice. On the issue thread the watch's baseline is the body read in this pass (none when
    /// the read failed); an edit made before the post is part of the baseline, not input.
    /// </summary>
    private async Task PostQuestionsAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        IssueStatus? status,
        Dictionary<int, IssueWatch> watches,
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
                ReplaceWatch(
                    watches,
                    IssueWatch.Start(
                        repository.Id,
                        info.IssueNumber,
                        WatchesBody(info) && status is not null ? IssueBodyHash.Of(status.Body) : null));
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

    private void ReplaceWatch(Dictionary<int, IssueWatch> watches, IssueWatch watch)
    {
        if (watches.Remove(watch.IssueNumber, out IssueWatch? previous))
        {
            dbContext.Set<IssueWatch>().Remove(previous);
        }

        dbContext.Set<IssueWatch>().Add(watch);
        watches[watch.IssueNumber] = watch;
    }

    // While a pull request is under review, ticking acceptance-criteria checkboxes edits the issue body, so the body
    // counts as input only on issue-thread waits (ADR 0096).
    private static bool WatchesBody(AwaitingInputIssueInfo info) => info.PullRequestUrl is null;

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
    /// Reads the question's thread after its post time (the eligible comments: not a bot, not system, not in a resolved
    /// thread, not in the posted-comment registry), records this pass's body on an issue-thread wait, and lets the watch
    /// decide. Returns the input once all of it has outlasted the quiet period; input still inside it schedules one
    /// follow-up nudge for when the last of it settles.
    /// </summary>
    private async Task<AwaitingInputArrived?> ReadInputAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        DateTimeOffset postedAt,
        PostedCommentIds postedComments,
        IssueWatch watch,
        IssueStatus? status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (WatchesBody(info))
        {
            if (status is null)
            {
                // Without this pass's body a pending change can't be confirmed, nor a revert seen; read again next pass.
                if (watch.PendingBodyChange is not null)
                {
                    return null;
                }
            }
            else
            {
                watch.ObserveBody(IssueBodyHash.Of(status.Body), now);
            }
        }

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

        switch (watch.Settle(feedback, now))
        {
            case InputSettlement.Settled settled:
                return new AwaitingInputArrived(repository.Id, info.IssueNumber, settled.Input, settled.ArrivedAt);

            case InputSettlement.Waiting waitingUntil:
                nudgeQueue.TryEnqueue(new Nudge(
                    new NudgeKey(repository.Id, NudgePass.AwaitingInput, info.IssueNumber),
                    waitingUntil.Until));
                return null;

            default:
                return null;
        }
    }
}
