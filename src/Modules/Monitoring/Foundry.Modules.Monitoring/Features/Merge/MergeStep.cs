using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Merge;

/// <summary>
/// The merge step (ADR 0105): for every merge-pending issue, merges the pull request Foundry opened through the
/// provider, pinned to the head its checks gate verified, and records a <see cref="MergeResolved"/> outcome. It runs
/// on every scheduled poll (the backstop) and on <see cref="NudgePass.Merge"/> nudges; its transient-failure
/// progress is persisted in <see cref="PendingMerge"/>, so a host restart resumes where it left off. Every outcome
/// other than <c>merged</c> sends the issue to review, where a person decides.
/// </summary>
internal sealed class MergeStep(
    IIssueQueries issueQueries,
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    WebhookNudgeQueue nudgeQueue,
    ILogger<MergeStep> logger)
{
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

        Dictionary<int, PendingMerge> progress = await dbContext.Set<PendingMerge>()
            .Where(m => m.MonitoredRepositoryId == repository.Id)
            .ToDictionaryAsync(m => m.IssueNumber, cancellationToken);

        if (issueNumber is null)
        {
            // Forget progress for issues that left merge-pending (resolved, closed or untracked).
            HashSet<int> pendingNumbers = pendingIssues.Select(i => i.IssueNumber).ToHashSet();
            dbContext.Set<PendingMerge>().RemoveRange(progress.Values.Where(m => !pendingNumbers.Contains(m.IssueNumber)));
        }

        List<IIntegrationEvent> events = [];

        foreach (MergePendingIssueInfo pending in pendingIssues)
        {
            if (issueNumber is { } targetNumber && pending.IssueNumber != targetNumber)
            {
                continue;
            }

            PendingMerge merge = TrackEpisode(repository, pending, progress);

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

            StepResult result = await StepSafelyAsync(repository, provider, pending, cancellationToken);
            Apply(repository, pending, merge, result, now, events);
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private PendingMerge TrackEpisode(
        MonitoredRepository repository,
        MergePendingIssueInfo pending,
        Dictionary<int, PendingMerge> progress)
    {
        if (!progress.TryGetValue(pending.IssueNumber, out PendingMerge? merge))
        {
            merge = PendingMerge.Start(repository.Id, pending.IssueNumber, pending.EnteredMergePendingAt);
            dbContext.Set<PendingMerge>().Add(merge);
            progress[pending.IssueNumber] = merge;
            return merge;
        }

        if (!merge.IsForEpisode(pending.EnteredMergePendingAt))
        {
            merge.Restart(pending.EnteredMergePendingAt);
        }

        return merge;
    }

    private void Apply(
        MonitoredRepository repository,
        MergePendingIssueInfo pending,
        PendingMerge merge,
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
                    Apply(repository, pending, merge, Skipped(MergeSkipReasons.ProviderError), now, events);
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
        CancellationToken cancellationToken)
    {
        try
        {
            return await StepAsync(repository, provider, pending, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        // The setting is read at merge time, not snapshotted when the issue entered merge-pending.
        if (!repository.MergeWhenChecksPass)
        {
            return new StepResult.Resolved(MergeOutcomes.Disabled);
        }

        StepResult? closed = await DetectClosedAsync(repository, provider, pending, cancellationToken);
        if (closed is not null)
        {
            return closed;
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
            MergeRequestOutcome.NotMergeable => Skipped(MergeSkipReasons.NotMergeable),
            MergeRequestOutcome.Unauthorized => Skipped(MergeSkipReasons.Unauthorized),
            MergeRequestOutcome.Unsupported => Skipped(MergeSkipReasons.NotMergeable),
            MergeRequestOutcome other => throw new UnreachableException(
                $"Unknown {nameof(MergeRequestOutcome)} variant: {other.GetType().Name}"),
        };
    }

    /// <summary>
    /// An issue closed while merge-pending completes it, and a pull request closed without merge fails it, as the
    /// rebase step does. A pull request someone else merged resolves <c>merged</c>: the work landed.
    /// </summary>
    private static async Task<StepResult?> DetectClosedAsync(
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
            return new StepResult.Transient(isClosedFailure.Error.Message);
        }

        if (isClosedResult is Result<bool>.Success { Value: true })
        {
            return new StepResult.Closed(new ProviderIssueClosed(repository.Id, pending.IssueNumber));
        }

        Result<PullRequestStatus> prStatusResult = await provider.GetPullRequestStatusAsync(
            repository.Slug,
            pending.PullRequestUrl,
            cancellationToken);

        if (prStatusResult is Result<PullRequestStatus>.Failure prStatusFailure)
        {
            return new StepResult.Transient(prStatusFailure.Error.Message);
        }

        PullRequestStatus status = ((Result<PullRequestStatus>.Success)prStatusResult).Value;
        return status switch
        {
            { IsMerged: true } => new StepResult.Resolved(MergeOutcomes.Merged),
            { IsClosed: true } => new StepResult.Closed(new ProviderPullRequestClosed(repository.Id, pending.IssueNumber)),
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
    }
}
