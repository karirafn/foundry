using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Merge;

/// <summary>
/// The merge step (ADR 0105): for every merge-pending issue, merges the pull request Foundry opened through the
/// provider, pinned to the head its checks gate verified, and records a <see cref="MergeResolved"/> outcome. It runs
/// on every scheduled poll (the backstop) and on <see cref="NudgePass.Merge"/> nudges; its transient-failure
/// progress is persisted in <see cref="PendingMerge"/>, so a host restart resumes where it left off. Every outcome
/// other than <c>merged</c> and <c>catch_up</c> sends the issue to review, where a person decides. Before it merges,
/// the step reads the pull request's review feedback: a person's comment stops the merge and drives a revision
/// instead (issue #917).
/// <para>
/// A merge refused because the pull request is behind the default branch joins the repository's catch-up queue
/// (<see cref="MergeQueue"/>, ADR 0111) instead of going to review. A waiting entry attempts no merge; once it heads
/// the queue and no up-to-date pull request is on its way to merging, it resolves <c>catch_up</c> and the issue
/// rebases, reruns its checks, and comes back to merge.
/// </para>
/// </summary>
internal sealed class MergeStep(
    IIssueQueries issueQueries,
    DbContext dbContext,
    PostedCommentRegistry postedCommentRegistry,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IIntegrationEventProcessor integrationEventProcessor,
    WebhookNudgeQueue nudgeQueue,
    ILogger<MergeStep> logger)
{
    /// <summary>
    /// The ref an ancestry read names for the default-branch head: <c>HEAD</c> resolves to it on both providers
    /// (GitHub <c>compare/HEAD...branch</c>, GitLab <c>repository/merge_base?refs[]=HEAD</c>).
    /// </summary>
    private const string DefaultBranchHead = "HEAD";

    /// <summary>
    /// Runs the step for every merge-pending issue of <paramref name="repository"/>, or only
    /// <paramref name="issueNumber"/> when supplied (nudged).
    /// </summary>
    public async Task RunAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MergePendingIssueInfo> pendingIssues = await issueQueries.GetMergePendingIssuesAsync(
            repository.Id,
            cancellationToken);
        IReadOnlyList<MergeQueueIssueInfo> queueIssues = await issueQueries.GetMergeQueueIssuesAsync(
            repository.Id,
            cancellationToken);

        List<PendingMerge> rows = await dbContext.Set<PendingMerge>()
            .Where(m => m.MonitoredRepositoryId == repository.Id)
            .ToListAsync(cancellationToken);
        MergeQueue queue = MergeQueue.From(repository.Id, rows);
        QueuePass pass = new(queue, MergeQueue.HeldIssueNumbers(pendingIssues, queueIssues), queueIssues);
        IReadOnlyDictionary<int, MergeQueueEntry> entriesBefore = queue.Entries(pass.HeldIssueNumbers);

        if (issueNumber is null)
        {
            dbContext.Set<PendingMerge>().RemoveRange(queue.Forget(pass.HeldIssueNumbers));
        }

        List<IIntegrationEvent> events = [];
        HashSet<int> resolvedIssueNumbers = [];
        PostedCommentIds? postedComments = null;
        bool merged = false;

        foreach (MergePendingIssueInfo pending in pendingIssues)
        {
            if (issueNumber is { } targetNumber && pending.IssueNumber != targetNumber)
            {
                continue;
            }

            TrackedMerge tracked = queue.Track(pending);
            if (tracked is TrackedMerge.Started started)
            {
                dbContext.Set<PendingMerge>().Add(started.Merge);
            }

            PendingMerge merge = tracked.Merge;

            if (merge.IsResolved)
            {
                // Resolved earlier; the issue leaves merge-pending once the outbox delivers the outcome.
                continue;
            }

            if (!merge.IsDue(now))
            {
                ScheduleNudge(repository, pending.IssueNumber, merge.NextAttemptAt);
                continue;
            }

            postedComments ??= await postedCommentRegistry.LoadAsync(repository.Id, cancellationToken);
            StepResult result = await StepSafelyAsync(
                repository,
                provider,
                pending,
                merge,
                pass,
                postedComments,
                cancellationToken);
            int eventCount = events.Count;
            Apply(repository, pending, merge, pass, result, now, events);
            if (events.Count > eventCount)
            {
                resolvedIssueNumbers.Add(pending.IssueNumber);
            }

            merged |= result is StepResult.Resolved { Outcome: MergeOutcomes.Merged };
        }

        // A merge moved the default branch: the queue head may start its catch-up now rather than at the next poll.
        if (merged && queue.Head(pass.HeldIssueNumbers) is { IsResolved: false } head
            && pendingIssues.Any(i => i.IssueNumber == head.IssueNumber))
        {
            ScheduleNudge(repository, head.IssueNumber, notBefore: null);
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<int> changed = ChangedEntries(queue, pass, entriesBefore, resolvedIssueNumbers);
        if (changed.Count > 0)
        {
            await PublishQueueChangedAsync(new MergeQueueChanged(repository.Id, changed), cancellationToken);
        }
    }

    /// <summary>
    /// The held issues whose queue entry reads differently than before the pass: a waiting entry joined, moved up,
    /// or changed the issue it waits for. Issues resolved in this pass are left out: their state change is broadcast
    /// once the outbox delivers the outcome.
    /// </summary>
    private static List<int> ChangedEntries(
        MergeQueue queue,
        QueuePass pass,
        IReadOnlyDictionary<int, MergeQueueEntry> entriesBefore,
        HashSet<int> resolvedIssueNumbers) =>
        queue.Entries(pass.HeldIssueNumbers)
            .Where(e => pass.HeldIssueNumbers.Contains(e.Key) && !resolvedIssueNumbers.Contains(e.Key))
            .Where(e => !entriesBefore.TryGetValue(e.Key, out MergeQueueEntry? before) || before != e.Value)
            .Select(e => e.Key)
            .Order()
            .ToList();

    /// <summary>
    /// Delivers <paramref name="event"/> directly, not through the outbox: it only refreshes an open issue pane, and
    /// the queue is persisted already.
    /// </summary>
    private async Task PublishQueueChangedAsync(MergeQueueChanged @event, CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventProcessor.ProcessAsync(Guid.NewGuid(), @event, cancellationToken);
        }
#pragma warning disable CA1031 // A failed refresh must not fail the merge pass; the next queue change refreshes the pane.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to deliver {EventType} for repository {RepositoryId} directly.",
                nameof(MergeQueueChanged),
                @event.RepositoryId.Value);
        }
    }

    private void Apply(
        MonitoredRepository repository,
        MergePendingIssueInfo pending,
        PendingMerge merge,
        QueuePass pass,
        StepResult result,
        DateTimeOffset now,
        List<IIntegrationEvent> events)
    {
        switch (result)
        {
            case StepResult.Resolved resolved:
                events.Add(new MergeResolved(repository.Id, pending.IssueNumber, resolved.Outcome, resolved.Reason));
                merge.MarkResolved(now);
                logger.LogInformation(
                    "Merge of issue #{IssueNumber} in '{Slug}' resolved {Outcome} {Reason}.",
                    pending.IssueNumber,
                    repository.Slug,
                    resolved.Outcome,
                    resolved.Reason);
                break;

            case StepResult.Closed closed:
                events.Add(closed.Event);
                merge.MarkResolved(now);
                break;

            case StepResult.Feedback feedback:
                events.Add(new MergeResolved(
                    repository.Id,
                    pending.IssueNumber,
                    MergeResolved.FeedbackOutcome,
                    Comments: feedback.Read.Comments,
                    OmittedCommentCount: feedback.Read.OmittedCommentCount,
                    NewestCommentAt: feedback.Read.NewestCommentAt));
                merge.MarkResolved(now);
                logger.LogInformation(
                    "Merge of issue #{IssueNumber} in '{Slug}' stopped by {CommentCount} review comment(s).",
                    pending.IssueNumber,
                    repository.Slug,
                    feedback.Read.Comments.Count);
                break;

            case StepResult.HeldBack heldBack:
                // A person's comment is still inside the quiet period: read again once it is actionable. Waiting on a
                // person is not a provider failure, so the transient budget is untouched.
                ScheduleNudge(repository, pending.IssueNumber, heldBack.Until);
                break;

            case StepResult.Behind:
                if (MergeQueue.RefuseBehind(merge, now) == BehindRefusal.FellBehind)
                {
                    Apply(repository, pending, merge, pass, Skipped(MergeSkipReasons.FellBehind), now, events);
                    return;
                }

                logger.LogInformation(
                    "Merge of issue #{IssueNumber} in '{Slug}' was refused because the pull request is behind; it waits in the catch-up queue after {Rounds} round(s).",
                    pending.IssueNumber,
                    repository.Slug,
                    merge.CatchUpRounds);

                if (pass.Queue.Head(pass.HeldIssueNumbers) == merge)
                {
                    ScheduleNudge(repository, pending.IssueNumber, notBefore: null);
                }

                break;

            case StepResult.CatchUp:
                events.Add(new MergeResolved(repository.Id, pending.IssueNumber, MergeResolved.CatchUpOutcome));
                merge.StartCatchUp(now);
                logger.LogInformation(
                    "Issue #{IssueNumber} in '{Slug}' heads the catch-up queue and starts catch-up round {Round}/{Max}.",
                    pending.IssueNumber,
                    repository.Slug,
                    merge.CatchUpRounds,
                    MergeQueue.MaxCatchUpRounds);
                break;

            case StepResult.Waiting waiting:
                merge.WaitFor(waiting.AwaitedIssueNumber);
                // Waiting is not a provider failure: the scheduled poll, or a merge nudge, evaluates the queue again.
                logger.LogDebug(
                    "Issue #{IssueNumber} in '{Slug}' waits in the catch-up queue: {Reason}.",
                    pending.IssueNumber,
                    repository.Slug,
                    waiting.Reason);
                break;

            case StepResult.Transient transient:
                bool exhausted = merge.RecordTransientFailure(now);
                logger.LogWarning(
                    "Merge of issue #{IssueNumber} in '{Slug}' hit a transient provider failure ({Attempt}/{Max}): {Error}",
                    pending.IssueNumber,
                    repository.Slug,
                    merge.TransientFailures,
                    PendingMerge.MaxTransientFailures,
                    transient.Message);

                if (exhausted)
                {
                    Apply(repository, pending, merge, pass, Skipped(MergeSkipReasons.ProviderError), now, events);
                    return;
                }

                ScheduleNudge(repository, pending.IssueNumber, merge.NextAttemptAt);
                break;
        }
    }


    private static async Task<StepResult> StepSafelyAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        PendingMerge merge,
        QueuePass pass,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await StepAsync(repository, provider, pending, merge, pass, postedComments, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new StepResult.Transient(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout.
            return new StepResult.Transient(ex.Message);
        }
    }

    private static async Task<StepResult> StepAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        PendingMerge merge,
        QueuePass pass,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        // The setting is read at merge time, not snapshotted when the issue entered merge-pending.
        if (!repository.MergeWhenChecksPass)
        {
            return new StepResult.Resolved(MergeOutcomes.Disabled);
        }

        PullRequestRead read = await ReadPullRequestAsync(repository, provider, pending, cancellationToken);
        if (read is PullRequestRead.Settled settled)
        {
            return settled.Result;
        }

        PullRequestStatus status = ((PullRequestRead.Open)read).Status;

        StepResult? feedback = await DetectFeedbackAsync(repository, provider, pending, postedComments, cancellationToken);
        if (feedback is not null)
        {
            return feedback;
        }

        // A queue entry waiting for its catch-up attempts no merge: it was refused as behind, and nothing it can do
        // brings it up to date but a catch-up (ADR 0111).
        if (merge.AwaitsCatchUp)
        {
            return await TryStartCatchUpAsync(repository, provider, pending, merge, pass, status, cancellationToken);
        }

        Result<MergeRequestOutcome> mergeResult = await provider.MergePullRequestAsync(
            repository.Slug,
            pending.PullRequestUrl,
            pending.HeadSha,
            cancellationToken);

        if (mergeResult is Result<MergeRequestOutcome>.Failure mergeFailure)
        {
            return new StepResult.Transient(mergeFailure.Error.Message);
        }

        return ((Result<MergeRequestOutcome>.Success)mergeResult).Value switch
        {
            MergeRequestOutcome.Merged => new StepResult.Resolved(MergeOutcomes.Merged),
            MergeRequestOutcome.HeadMoved => Skipped(MergeSkipReasons.HeadMoved),
            MergeRequestOutcome.NotMergeable => await ClassifyRefusalAsync(repository, provider, pending, cancellationToken),
            MergeRequestOutcome.Unauthorized => Skipped(MergeSkipReasons.Unauthorized),
            MergeRequestOutcome.Unsupported => Skipped(MergeSkipReasons.NotMergeable),
            MergeRequestOutcome other => throw new UnreachableException(
                $"Unknown {nameof(MergeRequestOutcome)} variant: {other.GetType().Name}"),
        };
    }

    /// <summary>
    /// Tells a refusal because the pull request is behind the default branch from any other refusal (ADR 0111): the
    /// provider applied the repository's own policy, so a branch that does not contain the default-branch head was
    /// refused for being behind. With <see cref="MonitoredRepository.RebaseAfterRun"/> off Foundry rewrites no
    /// branch, so the refusal stands without an ancestry read. A transient ancestry read counts against the merge's
    /// budget; any other failed read lets the refusal stand.
    /// </summary>
    private static async Task<StepResult> ClassifyRefusalAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        CancellationToken cancellationToken)
    {
        if (!repository.RebaseAfterRun)
        {
            return Skipped(MergeSkipReasons.NotMergeable);
        }

        Result<bool> upToDate = await provider.IsAncestorAsync(
            repository.Slug,
            DefaultBranchHead,
            pending.BranchName,
            cancellationToken);

        return upToDate switch
        {
            Result<bool>.Success { Value: false } => new StepResult.Behind(),
            Result<bool>.Failure failure when RunPublishingErrors.IsTransient(failure.Error) =>
                new StepResult.Transient(failure.Error.Message),
            _ => Skipped(MergeSkipReasons.NotMergeable),
        };
    }

    /// <summary>
    /// Starts a catch-up for a waiting queue entry once it heads the queue and no other pull request already up to
    /// date with the default branch is on its way to merging: that merge would make this catch-up's pipeline stale.
    /// Only a confirmed up-to-date answer holds the head; a failed read does not. A catch-up rebases the head the
    /// checks verified, so a head that moved since resolves <c>skipped(head_moved)</c>, as a refused pinned merge
    /// does.
    /// </summary>
    private static async Task<StepResult> TryStartCatchUpAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        PendingMerge merge,
        QueuePass pass,
        PullRequestStatus status,
        CancellationToken cancellationToken)
    {
        if (pass.Queue.Head(pass.HeldIssueNumbers) != merge)
        {
            return new StepResult.Waiting("not the queue head", AwaitedIssueNumber: null);
        }

        foreach (MergeQueueIssueInfo awaitable in pass.Queue.AwaitableIssues(merge, pass.QueueIssues))
        {
            Result<bool> upToDate = await provider.IsAncestorAsync(
                repository.Slug,
                DefaultBranchHead,
                awaitable.BranchName,
                cancellationToken);

            if (upToDate is Result<bool>.Success { Value: true })
            {
                return new StepResult.Waiting($"issue #{awaitable.IssueNumber} is already up to date", awaitable.IssueNumber);
            }
        }

        return status.HeadSha switch
        {
            null => new StepResult.Transient("The pull request's head commit could not be read."),
            { } headSha when string.Equals(headSha, pending.HeadSha, StringComparison.OrdinalIgnoreCase) =>
                new StepResult.CatchUp(),
            _ => Skipped(MergeSkipReasons.HeadMoved),
        };
    }

    /// <summary>
    /// An issue closed while merge-pending completes it, and a pull request closed without merge fails it, as the
    /// rebase step does. A pull request someone else merged resolves <c>merged</c>: the work landed. An open pull
    /// request returns its status for the steps that follow.
    /// </summary>
    private static async Task<PullRequestRead> ReadPullRequestAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        CancellationToken cancellationToken)
    {
        Result<bool> isClosedResult = await provider.IsIssueClosedAsync(
            repository.Slug,
            pending.IssueNumber,
            cancellationToken);

        if (isClosedResult is Result<bool>.Failure isClosedFailure)
        {
            return new PullRequestRead.Settled(new StepResult.Transient(isClosedFailure.Error.Message));
        }

        if (isClosedResult is Result<bool>.Success { Value: true })
        {
            return new PullRequestRead.Settled(
                new StepResult.Closed(new ProviderIssueClosed(repository.Id, pending.IssueNumber)));
        }

        Result<PullRequestStatus> prStatusResult = await provider.GetPullRequestStatusAsync(
            repository.Slug,
            pending.PullRequestUrl,
            cancellationToken);

        if (prStatusResult is Result<PullRequestStatus>.Failure prStatusFailure)
        {
            return new PullRequestRead.Settled(new StepResult.Transient(prStatusFailure.Error.Message));
        }

        PullRequestStatus status = ((Result<PullRequestStatus>.Success)prStatusResult).Value;
        return status switch
        {
            { IsMerged: true } => new PullRequestRead.Settled(new StepResult.Resolved(MergeOutcomes.Merged)),
            { IsClosed: true } => new PullRequestRead.Settled(
                new StepResult.Closed(new ProviderPullRequestClosed(repository.Id, pending.IssueNumber))),
            _ => new PullRequestRead.Open(status),
        };
    }

    /// <summary>
    /// Reads the pull request's review feedback since the issue's cutoff, with the review pass's actionable-feedback
    /// policy (issue #917). Actionable comments stop the merge, and win over comments still held back; held-back
    /// comments alone postpone it. A failed read is transient and counts against the merge's budget.
    /// </summary>
    private static async Task<StepResult?> DetectFeedbackAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        MergePendingIssueInfo pending,
        PostedCommentIds postedComments,
        CancellationToken cancellationToken)
    {
        Result<ReviewFeedback> feedbackResult = await provider.GetReviewFeedbackAsync(
            repository.Slug,
            pending.PullRequestUrl,
            pending.FeedbackCutoffAt,
            postedComments,
            cancellationToken);

        if (feedbackResult is Result<ReviewFeedback>.Failure feedbackFailure)
        {
            return new StepResult.Transient(feedbackFailure.Error.Message);
        }

        ReviewFeedback feedback = ((Result<ReviewFeedback>.Success)feedbackResult).Value;
        return feedback switch
        {
            { Comments.Count: > 0 } => new StepResult.Feedback(feedback),
            { HeldBackUntil: { } heldBackUntil } => new StepResult.HeldBack(heldBackUntil),
            _ => null,
        };
    }

    private void ScheduleNudge(MonitoredRepository repository, int issueNumber, DateTimeOffset? notBefore)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(repository.Id, NudgePass.Merge, issueNumber), notBefore));
    }

    private static StepResult.Resolved Skipped(string reason) => new(MergeOutcomes.Skipped, reason);

    /// <summary>The wire discriminators of <see cref="MergeResolved.Outcome"/>.</summary>
    internal static class MergeOutcomes
    {
        public const string Merged = "merged";
        public const string Disabled = "disabled";
        public const string Skipped = "skipped";
    }

    private abstract record StepResult
    {
        public sealed record Resolved(string Outcome, string? Reason = null) : StepResult;

        public sealed record Closed(IIntegrationEvent Event) : StepResult;

        public sealed record Transient(string Message) : StepResult;

        /// <summary>A person's actionable review feedback stopped the merge.</summary>
        public sealed record Feedback(ReviewFeedback Read) : StepResult;

        /// <summary>A person's comment waits out the quiet period until <see cref="Until"/>; no merge before then.</summary>
        public sealed record HeldBack(DateTimeOffset Until) : StepResult;

        /// <summary>The merge was refused because the pull request is behind the default branch (ADR 0111).</summary>
        public sealed record Behind : StepResult;

        /// <summary>The waiting queue head starts a catch-up round.</summary>
        public sealed record CatchUp : StepResult;

        /// <summary>
        /// The queue entry keeps waiting for its catch-up, for <see cref="Reason"/>; <see cref="AwaitedIssueNumber"/>
        /// names the up-to-date issue it waits for, if any.
        /// </summary>
        public sealed record Waiting(string Reason, int? AwaitedIssueNumber) : StepResult;
    }

    /// <summary>The pull request read that precedes every merge decision.</summary>
    private abstract record PullRequestRead
    {
        /// <summary>The issue or pull request closed, merged, or could not be read: the step stops with this result.</summary>
        public sealed record Settled(StepResult Result) : PullRequestRead;

        /// <summary>The pull request is open, at <see cref="PullRequestStatus.HeadSha"/>.</summary>
        public sealed record Open(PullRequestStatus Status) : PullRequestRead;
    }

    /// <summary>
    /// The repository's catch-up queue for one pass: its rows, the issues whose rows keep their place, and the issues
    /// the queue head may wait for.
    /// </summary>
    private sealed record QueuePass(
        MergeQueue Queue,
        IReadOnlySet<int> HeldIssueNumbers,
        IReadOnlyList<MergeQueueIssueInfo> QueueIssues);
}
