using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Rebase;

/// <summary>
/// The post-run rebase step (ADR 0087): for every rebase-pending issue, rebases the pull request branch onto
/// its base through the provider and records a <see cref="RebaseResolved"/> outcome once it lands, fails or is
/// skipped. It runs on every scheduled poll (the backstop) and on <see cref="NudgePass.Rebase"/> nudges; its
/// progress is persisted in <see cref="PendingRebase"/>, so a host restart resumes where it left off.
/// Every failure fails open: the issue moves on to the checks gate with a <c>skipped</c> outcome.
/// </summary>
internal sealed class RebaseStep(
    IIssueQueries issueQueries,
    DbContext dbContext,
    IIntegrationEventDispatcher integrationEventDispatcher,
    WebhookNudgeQueue nudgeQueue,
    ILogger<RebaseStep> logger)
{
    /// <summary>
    /// How many times one step re-reads the head and retries after a stale <c>expectedHeadOid</c> — a push
    /// landed between the read and the rebase call — before skipping with <c>stale-head</c>.
    /// </summary>
    internal const int MaxStaleHeadAttempts = 3;

    /// <summary>
    /// Runs the step for every rebase-pending issue of <paramref name="repository"/>, or only
    /// <paramref name="issueNumber"/> when supplied (nudged).
    /// </summary>
    public async Task RunAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        int? issueNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RebasePendingIssueInfo> pendingIssues = await issueQueries.GetRebasePendingIssuesAsync(
            repository.Id,
            cancellationToken);

        Dictionary<int, PendingRebase> progress = await dbContext.Set<PendingRebase>()
            .Where(r => r.MonitoredRepositoryId == repository.Id)
            .ToDictionaryAsync(r => r.IssueNumber, cancellationToken);

        if (issueNumber is null)
        {
            // Forget progress for issues that left rebase-pending (resolved, closed or untracked).
            HashSet<int> pendingNumbers = pendingIssues.Select(i => i.IssueNumber).ToHashSet();
            dbContext.Set<PendingRebase>().RemoveRange(progress.Values.Where(r => !pendingNumbers.Contains(r.IssueNumber)));
        }

        List<IIntegrationEvent> events = [];

        // HTTP calls are sequential: rebase-pending issues per repository are expected to be small (design decision D6).
        foreach (RebasePendingIssueInfo pending in pendingIssues)
        {
            if (issueNumber is { } targetNumber && pending.IssueNumber != targetNumber)
            {
                continue;
            }

            PendingRebase rebase = TrackEpisode(repository, pending, progress);

            if (rebase.IsResolved)
            {
                // Resolved earlier; the issue leaves rebase-pending once the outbox delivers the outcome.
                continue;
            }

            if (!rebase.IsDue(now))
            {
                ScheduleNudge(repository, pending.IssueNumber, rebase.NextAttemptAt);
                continue;
            }

            StepResult result = await StepSafelyAsync(repository, provider, pending, rebase, now, cancellationToken);
            Apply(repository, pending, rebase, result, now, events);
        }

        await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private PendingRebase TrackEpisode(
        MonitoredRepository repository,
        RebasePendingIssueInfo pending,
        Dictionary<int, PendingRebase> progress)
    {
        if (!progress.TryGetValue(pending.IssueNumber, out PendingRebase? rebase))
        {
            rebase = PendingRebase.Start(repository.Id, pending.IssueNumber, pending.EnteredRebasePendingAt);
            dbContext.Set<PendingRebase>().Add(rebase);
            progress[pending.IssueNumber] = rebase;
            return rebase;
        }

        if (!rebase.IsForEpisode(pending.EnteredRebasePendingAt))
        {
            rebase.Restart(pending.EnteredRebasePendingAt);
        }

        return rebase;
    }

    private void Apply(
        MonitoredRepository repository,
        RebasePendingIssueInfo pending,
        PendingRebase rebase,
        StepResult result,
        DateTimeOffset now,
        List<IIntegrationEvent> events)
    {
        switch (result)
        {
            case StepResult.Resolved resolved:
                events.Add(new RebaseResolved(repository.Id, pending.IssueNumber, resolved.Outcome, resolved.Reason));
                rebase.MarkResolved(now);
                logger.LogInformation(
                    "Rebase of issue #{IssueNumber} in '{Slug}' resolved {Outcome} {Reason}.",
                    pending.IssueNumber,
                    repository.Slug,
                    resolved.Outcome,
                    resolved.Reason);
                break;

            case StepResult.Closed closed:
                events.Add(closed.Event);
                rebase.MarkResolved(now);
                break;

            case StepResult.Waiting:
                ScheduleNudge(repository, pending.IssueNumber, rebase.NextAttemptAt);
                break;

            case StepResult.Idle:
                break;

            case StepResult.Transient transient:
                if (rebase.HasLandingTimedOut(now))
                {
                    // The landing bound outranks the retry budget: a rebase still unobserved after it is skipped.
                    Apply(repository, pending, rebase, Skipped(RebaseSkipReasons.Timeout), now, events);
                    return;
                }

                bool exhausted = rebase.RecordTransientFailure(now);
                logger.LogWarning(
                    "Rebase of issue #{IssueNumber} in '{Slug}' hit a transient provider failure ({Attempt}/{Max}): {Error}",
                    pending.IssueNumber,
                    repository.Slug,
                    rebase.TransientFailures,
                    PendingRebase.MaxTransientFailures,
                    transient.Message);

                if (exhausted)
                {
                    Apply(repository, pending, rebase, Skipped(RebaseSkipReasons.ProviderError), now, events);
                    return;
                }

                ScheduleNudge(repository, pending.IssueNumber, rebase.NextAttemptAt);
                break;
        }
    }

    private static async Task<StepResult> StepSafelyAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        RebasePendingIssueInfo pending,
        PendingRebase rebase,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            return await StepAsync(repository, provider, pending, rebase, now, cancellationToken);
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
        RebasePendingIssueInfo pending,
        PendingRebase rebase,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The toggle is read at rebase time, not snapshotted when the issue entered rebase-pending.
        if (!repository.RebaseAfterRun)
        {
            return new StepResult.Resolved(RebaseOutcomes.Disabled);
        }

        StepResult? closed = await DetectClosedAsync(repository, provider, pending, cancellationToken);
        if (closed is not null)
        {
            return closed;
        }

        return rebase.IsAwaitingLanding
            ? await AwaitLandingAsync(repository, provider, pending, rebase, now, cancellationToken)
            : await RequestRebaseAsync(repository, provider, pending, rebase, now, cancellationToken);
    }

    /// <summary>
    /// A pull request (or issue) closed while rebase-pending takes the same Complete / Fail transitions as a
    /// checks-pending one. A merged pull request raises nothing: the issue completes when its provider issue closes.
    /// </summary>
    private static async Task<StepResult?> DetectClosedAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        RebasePendingIssueInfo pending,
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

        if (prStatusResult is Result<PullRequestStatus>.Success { Value: { IsClosed: true, IsMerged: false } })
        {
            return new StepResult.Closed(new ProviderPullRequestClosed(repository.Id, pending.IssueNumber));
        }

        if (prStatusResult is Result<PullRequestStatus>.Success { Value.IsClosed: true })
        {
            // Merged: nothing to rebase; the issue completes on a later poll when its provider issue closes.
            return new StepResult.Idle();
        }

        return null;
    }

    private static async Task<StepResult> RequestRebaseAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        RebasePendingIssueInfo pending,
        PendingRebase rebase,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxStaleHeadAttempts; attempt++)
        {
            Result<PullRequestRebaseState> stateResult = await provider.GetRebaseStateAsync(
                repository.Slug,
                pending.PullRequestUrl,
                cancellationToken);

            if (stateResult is Result<PullRequestRebaseState>.Failure stateFailure)
            {
                return new StepResult.Transient(stateFailure.Error.Message);
            }

            PullRequestRebaseState state = ((Result<PullRequestRebaseState>.Success)stateResult).Value;
            switch (state)
            {
                case PullRequestRebaseState.Unsupported:
                    return Skipped(RebaseSkipReasons.Unsupported);
                case PullRequestRebaseState.Refused refused:
                    return Skipped(refused.Reason);
                case PullRequestRebaseState.UpToDate:
                    // Compare first: a branch with nothing to catch up is never rebased.
                    return new StepResult.Resolved(RebaseOutcomes.UpToDate);
            }

            PullRequestRebaseState.Behind behind = (PullRequestRebaseState.Behind)state;

            Result<RebaseRequestOutcome> requestResult = await provider.RequestRebaseAsync(
                repository.Slug,
                behind.PullRequestId,
                behind.HeadSha,
                cancellationToken);

            if (requestResult is Result<RebaseRequestOutcome>.Failure requestFailure)
            {
                return new StepResult.Transient(requestFailure.Error.Message);
            }

            switch (((Result<RebaseRequestOutcome>.Success)requestResult).Value)
            {
                case RebaseRequestOutcome.Requested:
                    // The provider rebases asynchronously: wait for a head other than the pre-rebase one.
                    rebase.RecordRequested(behind.HeadSha, now);
                    return new StepResult.Waiting();
                case RebaseRequestOutcome.UpToDate:
                    return new StepResult.Resolved(RebaseOutcomes.UpToDate);
                case RebaseRequestOutcome.Conflict:
                    return new StepResult.Resolved(RebaseOutcomes.Conflict);
                case RebaseRequestOutcome.Refused refused:
                    return Skipped(refused.Reason);
                case RebaseRequestOutcome.Unsupported:
                    return Skipped(RebaseSkipReasons.Unsupported);
                case RebaseRequestOutcome.StaleHead:
                    // A push landed between the read and the call: re-read the head and retry.
                    continue;
            }
        }

        return Skipped(RebaseSkipReasons.StaleHead);
    }

    private static async Task<StepResult> AwaitLandingAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        RebasePendingIssueInfo pending,
        PendingRebase rebase,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<PullRequestRebaseState> stateResult = await provider.GetRebaseStateAsync(
            repository.Slug,
            pending.PullRequestUrl,
            cancellationToken);

        if (stateResult is Result<PullRequestRebaseState>.Failure stateFailure)
        {
            return new StepResult.Transient(stateFailure.Error.Message);
        }

        string? headSha = ((Result<PullRequestRebaseState>.Success)stateResult).Value switch
        {
            PullRequestRebaseState.UpToDate upToDate => upToDate.HeadSha,
            PullRequestRebaseState.Behind behind => behind.HeadSha,
            _ => null,
        };

        if (headSha is not null && !string.Equals(headSha, rebase.PreRebaseHeadSha, StringComparison.OrdinalIgnoreCase))
        {
            return new StepResult.Resolved(RebaseOutcomes.Rebased);
        }

        if (rebase.HasLandingTimedOut(now))
        {
            return Skipped(RebaseSkipReasons.Timeout);
        }

        if (headSha is null)
        {
            // The provider stopped answering with a head (refused or unsupported): skip rather than wait it out.
            return ((Result<PullRequestRebaseState>.Success)stateResult).Value is PullRequestRebaseState.Refused refused
                ? Skipped(refused.Reason)
                : Skipped(RebaseSkipReasons.Unsupported);
        }

        rebase.RecordAwaitingLanding(now);
        return new StepResult.Waiting();
    }

    private void ScheduleNudge(MonitoredRepository repository, int issueNumber, DateTimeOffset? notBefore)
    {
        nudgeQueue.TryEnqueue(new Nudge(new NudgeKey(repository.Id, NudgePass.Rebase, issueNumber), notBefore));
    }

    private static StepResult.Resolved Skipped(string reason) => new(RebaseOutcomes.Skipped, reason);

    /// <summary>The wire discriminators of <see cref="RebaseResolved.Outcome"/>.</summary>
    internal static class RebaseOutcomes
    {
        public const string UpToDate = "up_to_date";
        public const string Rebased = "rebased";
        public const string Disabled = "disabled";
        public const string Skipped = "skipped";
        public const string Conflict = "conflict";
    }

    private abstract record StepResult
    {
        public sealed record Resolved(string Outcome, string? Reason = null) : StepResult;

        public sealed record Closed(IIntegrationEvent Event) : StepResult;

        public sealed record Waiting : StepResult;

        public sealed record Idle : StepResult;

        public sealed record Transient(string Message) : StepResult;
    }
}
