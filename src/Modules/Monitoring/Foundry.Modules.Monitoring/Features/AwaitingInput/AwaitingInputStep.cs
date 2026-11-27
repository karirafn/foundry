using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.PullRequestWatch;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Watch = Foundry.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatch;

namespace Foundry.Modules.Monitoring.Features.AwaitingInput;

/// <summary>
/// The awaiting-input step (ADR 0096): for every awaiting-input issue it first applies the review pass's ways out (the
/// issue closed, or its pull request closed without merging), then posts the questions while they are unposted, or
/// once they are posted reads the question's thread, on an issue-thread wait the issue body, and on a wait with a resume
/// branch the branch head, and lets the issue's <see cref="IssueWatch"/> decide whether the round's input has arrived. A posted question that has waited the reminder
/// delay with no input gets one reminder in the same thread. It runs on every scheduled poll and on
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

            PullRequestWayOut? wayOut = await ReadWayOutAsync(repository, provider, info, status, cancellationToken);

            if (FindWayOut(repository, info, status, wayOut) is { } wayOutEvent)
            {
                events.Add(wayOutEvent);
                continue;
            }

            switch (info.PostState)
            {
                case AwaitingInputPostState.Unposted { DueAt: DateTimeOffset dueAt } when dueAt <= now:
                    BranchHead? headBeforePost = await ReadHeadAsync(repository, provider, info, wayOut, cancellationToken);
                    await PostQuestionsAsync(repository, provider, info, status, headBeforePost, watches, now, cancellationToken);
                    break;

                case AwaitingInputPostState.Posted posted:
                    IssueWatch watch = WatchOf(repository, info, watches);
                    BranchHead? head = await ReadHeadAsync(repository, provider, info, wayOut, cancellationToken);
                    switch (await ReadInputAsync(repository, provider, info, posted.PostedAt, postedComments, watch, status, head, now, cancellationToken))
                    {
                        case InputSettlement.Settled settled:
                            events.Add(new AwaitingInputArrived(repository.Id, info.IssueNumber, settled.Input, settled.ArrivedAt));
                            dbContext.Set<IssueWatch>().Remove(watch);
                            watches.Remove(info.IssueNumber);
                            break;

                        case InputSettlement.Waiting waitingUntil:
                            nudgeQueue.TryEnqueue(new Nudge(
                                new NudgeKey(repository.Id, NudgePass.AwaitingInput, info.IssueNumber),
                                waitingUntil.Until));
                            break;

                        // Only a wait with no input at all is reminded: input inside its quiet period is about to resume it.
                        case InputSettlement.NothingNew when posted.ReminderDueAt is { } reminderDueAt && reminderDueAt <= now:
                            await PostReminderAsync(repository, provider, info, posted, now, cancellationToken);
                            break;
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
    /// first successful body and head reads become the baselines rather than changes. A watch left from a failed triage the
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

        IssueWatch started = IssueWatch.Start(repository.Id, info.IssueNumber, baselineBodyHash: null, baselineHead: null);
        ReplaceWatch(watches, started);
        return started;
    }

    /// <summary>
    /// Reads the pull request's way out when the question lives on one and the issue read succeeded; an open pull
    /// request's head is the revision's branch head, so the head costs no extra request. Returns
    /// <see langword="null"/> otherwise or when the read failed (retried on the next run).
    /// </summary>
    private static async Task<PullRequestWayOut?> ReadWayOutAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        IssueStatus? status,
        CancellationToken cancellationToken)
    {
        if (info.PullRequestUrl is not { } pullRequestUrl || status is null)
        {
            return null;
        }

        return await Watch.ReadAsync(provider, repository.Slug, status, pullRequestUrl, cancellationToken)
            is Result<PullRequestWayOut>.Success { Value: var wayOut }
            ? wayOut
            : null;
    }

    /// <summary>
    /// The review pass's ways out: a closed issue completes; a pull request closed without merging fails continuably;
    /// a merged or open pull request waits for its issue to close. Returns <see langword="null"/> while the issue keeps
    /// waiting or a read failed (retried on the next run). A wait without a pull request has only its issue's closure.
    /// </summary>
    private static IIntegrationEvent? FindWayOut(
        MonitoredRepository repository,
        AwaitingInputIssueInfo info,
        IssueStatus? status,
        PullRequestWayOut? wayOut) =>
        wayOut switch
        {
            PullRequestWayOut.IssueClosed => new ProviderIssueClosed(repository.Id, info.IssueNumber),
            PullRequestWayOut.PullRequestClosed => new ProviderPullRequestClosed(repository.Id, info.IssueNumber),
            null when status is { IsClosed: true } =>
                new ProviderIssueClosed(repository.Id, info.IssueNumber),
            _ => null,
        };

    /// <summary>
    /// Reads the resume branch's head (ADR 0096): a revision's is the head of the open pull request already read, a
    /// continuation's takes one branch read. Returns <see langword="null"/> when the wait has no branch or the read
    /// failed; a pull request read that found no open pull request reads no head.
    /// </summary>
    private static async Task<BranchHead?> ReadHeadAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        PullRequestWayOut? wayOut,
        CancellationToken cancellationToken)
    {
        if (info.BranchName is not { } branchName)
        {
            return null;
        }

        if (info.PullRequestUrl is not null)
        {
            return wayOut is PullRequestWayOut.Open open ? new BranchHead.Present(open.Head.Value) : null;
        }

        return await provider.GetBranchHeadAsync(repository.Slug, branchName, cancellationToken)
            is Result<BranchHead>.Success { Value: var head }
            ? head
            : null;
    }

    /// <summary>
    /// Posts the questions on the pull request when there is one, otherwise on the issue. A posted question is saved at
    /// once, together with its posted-comment registry entry and a fresh <see cref="IssueWatch"/>, so a later failure in
    /// this run never posts it twice. On the issue thread the watch's body baseline is the body read in this pass, and
    /// on a wait with a resume branch its head baseline is <paramref name="head"/> (each none when its read failed); an
    /// edit or push made before the post is part of the baseline, not input.
    /// </summary>
    private async Task PostQuestionsAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        IssueStatus? status,
        BranchHead? head,
        Dictionary<int, IssueWatch> watches,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<ProviderCommentId> result = await PostSafelyAsync(
            repository,
            provider,
            ThreadOf(info),
            QuestionComment.Compose(info.Questions, info.Decisions),
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
                        WatchesBody(info) && status is not null ? IssueBodyHash.Of(status.Body) : null,
                        head));
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

    /// <summary>
    /// Posts the round's one reminder in the question's thread. A posted reminder is saved at once with its
    /// posted-comment registry entry, so it is never read as a reply and never posted twice; a failed one retries with
    /// the question post's backoff.
    /// </summary>
    private async Task PostReminderAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        AwaitingInputPostState.Posted posted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<ProviderCommentId> result = await PostSafelyAsync(
            repository,
            provider,
            ThreadOf(info),
            QuestionComment.ComposeReminder(posted.CommentUrl),
            cancellationToken);

        switch (result)
        {
            case Result<ProviderCommentId>.Success success:
                postedCommentRegistry.Record(repository.Id, success.Value, now);
                await awaitingInputCommands.RecordReminderPostedAsync(info.IssueId, success.Value.Value, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Posted the reminder of awaiting-input issue #{IssueNumber} in '{Slug}' as comment {CommentId}.",
                    info.IssueNumber,
                    repository.Slug,
                    success.Value);
                break;

            case Result<ProviderCommentId>.Failure failure:
                await awaitingInputCommands.RecordReminderPostFailedAsync(
                    info.IssueId,
                    failure.Error.Message,
                    CommentPostErrors.IsTransient(failure.Error),
                    now,
                    cancellationToken);
                logger.LogWarning(
                    "Posting the reminder of awaiting-input issue #{IssueNumber} in '{Slug}' failed: {Error}",
                    info.IssueNumber,
                    repository.Slug,
                    failure.Error.Message);
                break;
        }
    }

    private static CommentThread ThreadOf(AwaitingInputIssueInfo info) =>
        info.PullRequestUrl is { } pullRequestUrl
            ? new CommentThread.PullRequest(pullRequestUrl)
            : new CommentThread.Issue(info.IssueNumber);

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
    /// thread, not in the posted-comment registry), records this pass's body on an issue-thread wait and its head on a
    /// wait with a resume branch, and lets the watch decide. Returns <see langword="null"/> when this pass can't tell (a read failed).
    /// </summary>
    private async Task<InputSettlement?> ReadInputAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        AwaitingInputIssueInfo info,
        DateTimeOffset postedAt,
        PostedCommentIds postedComments,
        IssueWatch watch,
        IssueStatus? status,
        BranchHead? head,
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

        if (info.BranchName is not null)
        {
            if (head is null)
            {
                // Without this pass's head a pending push can't be confirmed, nor a revert seen; read again next pass.
                if (watch.PendingHeadChange is not null)
                {
                    return null;
                }
            }
            else
            {
                watch.ObserveHead(head, now);
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

        return watch.Settle(feedback, now);
    }
}
