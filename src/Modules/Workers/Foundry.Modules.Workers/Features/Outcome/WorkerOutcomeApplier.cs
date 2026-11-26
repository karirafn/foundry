using System.Diagnostics;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.Modules.Workers.Features.HostPauses;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using DispatchPausedEvent = Foundry.Modules.Workers.Contracts.DispatchPaused;
using WorkerAuthenticationFailedEvent = Foundry.Modules.Workers.Contracts.WorkerAuthenticationFailed;
using WorkerCreditsExhaustedEvent = Foundry.Modules.Workers.Contracts.WorkerCreditsExhausted;
using ReviewRunCompletedEvent = Foundry.Modules.Workers.Contracts.ReviewRunCompleted;
using TriageRunCompletedEvent = Foundry.Modules.Workers.Contracts.TriageRunCompleted;
using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Applies the side effects of a resolved <see cref="WorkerOutcome"/>: the terminal run
/// transition, integration events, usage-limit/auth/credits side effects, and container cleanup.
/// Invariant: every terminal outcome stops and removes the run's container.
/// </summary>
internal sealed class WorkerOutcomeApplier(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IIntegrationEventProcessor integrationEventProcessor,
    IDomainEventDispatcher domainEventDispatcher,
    ISystemNotificationBroadcaster broadcaster,
    TimeProvider timeProvider,
    ILogger<WorkerOutcomeApplier> logger)
{
    /// <summary>
    /// Applies side effects for a resolved <see cref="WorkerOutcome"/>: state transition,
    /// integration event dispatch, usage-limit persistence, and container stop+remove.
    /// <see cref="WorkerOutcome.Indeterminate"/> must be handled by the caller before
    /// invoking this method.
    /// </summary>
    public async Task ApplyAsync(
        WorkerOutcome outcome,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        await ApplyAsync(outcome, activeRun, verdict: null, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="outcome"/> as <see cref="ApplyAsync(WorkerOutcome, ActiveRun, CancellationToken)"/> does, and
    /// records <paramref name="verdict"/> on the terminal run. The applier only records the verdict; the resolver already turned a <c>needs_input</c> verdict into its outcome.
    /// </summary>
    public async Task ApplyAsync(
        WorkerOutcome outcome,
        ActiveRun activeRun,
        RunVerdict? verdict,
        CancellationToken cancellationToken)
    {
        await ApplyAsync(outcome, activeRun, verdict, transcript: null, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="outcome"/> and <paramref name="verdict"/>, and stores <paramref name="transcript"/>, when
    /// captured, in the same save as the terminal transition.
    /// </summary>
    public async Task ApplyAsync(
        WorkerOutcome outcome,
        ActiveRun activeRun,
        RunVerdict? verdict,
        WorkerRunTranscript? transcript,
        CancellationToken cancellationToken)
    {
        await ApplyCoreAsync(outcome, activeRun, verdict, transcript, cancellationToken);
        await TryStopAndRemoveAsync(activeRun.ContainerId.Value, activeRun.Id, cancellationToken);
    }

    /// <summary>
    /// Applies the outcome of a run whose commits were published (ADR 0100). Its container is already gone, and its
    /// <see cref="WorkerRunCompletedEvent"/> carries the publish facts.
    /// </summary>
    public Task ApplyAsync(WorkerOutcome outcome, PublishingRun publishingRun, CancellationToken cancellationToken) =>
        ApplyCoreAsync(outcome, publishingRun, publishingRun.Verdict, publishingRun.TranscriptForTerminalState(), cancellationToken);

    private async Task ApplyCoreAsync<TRun>(
        WorkerOutcome outcome,
        TRun activeRun,
        RunVerdict? verdict,
        WorkerRunTranscript? transcript,
        CancellationToken cancellationToken)
        where TRun : WorkerRun, ITerminableRun
    {
        IReadOnlyCollection<object> alongside = transcript is null ? [] : [transcript];

        switch (outcome)
        {
            case WorkerOutcome.Completed completed:
            {
                CompletedRun completedRun = activeRun.Complete(
                    0,
                    completed.BranchName,
                    completed.PullRequestUrl,
                    completed.Summary,
                    verdict,
                    completed.ContainerOutput);
                // Enqueue before TransitionAsync: the trailing harvest SaveChangesAsync inside
                // TransitionAsync drains the outbox collector atomically with the state change.
                await TryDispatchAsync(
                    [new WorkerRunCompletedEvent(
                        activeRun.Id,
                        activeRun.IssueId.Value,
                        completed.BranchName.Value,
                        completed.PullRequestUrl.Value,
                        WorkerRunMergeState.Merged,
                        RunStartedAt: activeRun.StartedAt,
                        Publish: PublishFactsOf(activeRun, completed.PullRequestUrl.Value))],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, completedRun, alongside, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} completed (merged, PR: {PrUrl}, branch: {BranchName}).",
                    activeRun.Id,
                    completed.PullRequestUrl.Value,
                    completed.BranchName.Value);
                break;
            }

            case WorkerOutcome.Review review:
            {
                CompletedRun reviewRun = activeRun.Complete(
                    0,
                    review.BranchName,
                    review.PullRequestUrl,
                    review.Summary,
                    verdict,
                    review.ContainerOutput);
                await TryDispatchAsync(
                    [new WorkerRunCompletedEvent(
                        activeRun.Id,
                        activeRun.IssueId.Value,
                        review.BranchName.Value,
                        review.PullRequestUrl.Value,
                        WorkerRunMergeState.Open,
                        RunStartedAt: activeRun.StartedAt,
                        Publish: PublishFactsOf(activeRun, review.PullRequestUrl.Value))],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, reviewRun, alongside, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} completed with open PR: {PrUrl} (branch: {BranchName}).",
                    activeRun.Id,
                    review.PullRequestUrl.Value,
                    review.BranchName.Value);
                break;
            }

            case WorkerOutcome.Unchanged unchanged:
            {
                CompletedRun unchangedRun = activeRun.Complete(
                    0,
                    unchanged.BranchName,
                    null,
                    unchanged.Summary,
                    verdict,
                    unchanged.ContainerOutput);
                await TryDispatchAsync(
                    [new WorkerRunCompletedEvent(
                        activeRun.Id,
                        activeRun.IssueId.Value,
                        unchanged.BranchName.Value,
                        null,
                        WorkerRunMergeState.None,
                        RunStartedAt: activeRun.StartedAt,
                        Publish: PublishFactsOf(activeRun, null))],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, unchangedRun, alongside, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} completed with no commits (unchanged, branch: {BranchName}).",
                    activeRun.Id,
                    unchanged.BranchName.Value);
                break;
            }

            case WorkerOutcome.NeedsInput needsInput:
            {
                CompletedRun needsInputRun = activeRun.Complete(
                    0,
                    needsInput.BranchName,
                    needsInput.PullRequestUrl,
                    needsInput.Summary,
                    verdict,
                    needsInput.ContainerOutput);
                await TryDispatchAsync(
                    [new WorkerRunCompletedEvent(
                        activeRun.Id,
                        activeRun.IssueId.Value,
                        needsInput.BranchName.Value,
                        needsInput.PullRequestUrl?.Value,
                        needsInput.PullRequestUrl is null ? WorkerRunMergeState.None : WorkerRunMergeState.Open,
                        RunStartedAt: activeRun.StartedAt,
                        InputRequest: new WorkerInputRequest(
                            [.. needsInput.Questions],
                            needsInput.BranchHasCommits),
                        Publish: PublishFactsOf(activeRun, needsInput.PullRequestUrl?.Value))],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, needsInputRun, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} stopped to ask {QuestionCount} question(s) (branch: {BranchName}).",
                    activeRun.Id,
                    needsInput.Questions.Count,
                    needsInput.BranchName.Value);
                break;
            }

            case WorkerOutcome.TriageCompleted triage:
            {
                // A triage run publishes its verdict instead of WorkerRunCompleted: it has no branch or merge request.
                CompletedRun triageRun = activeRun.Complete(
                    0,
                    branchName: null,
                    pullRequestUrl: null,
                    triage.Summary,
                    verdict,
                    triage.ContainerOutput);
                await TryDispatchAsync(
                    [new TriageRunCompletedEvent(
                        activeRun.IssueId.Value,
                        activeRun.Id,
                        ToTriageResult(triage.Verdict),
                        timeProvider.GetUtcNow(),
                        activeRun.SuppliedSpecHash)],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, triageRun, alongside, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} triaged its issue: {Outcome}.",
                    activeRun.Id,
                    triage.Verdict.GetType().Name);
                break;
            }

            case WorkerOutcome.ReviewCompleted reviewed:
            {
                // A review run publishes its checked verdict instead of WorkerRunCompleted: it has no branch or merge request.
                RecordedReviewVerdict recorded = reviewed.Verdict.ToRecorded();
                CompletedRun reviewedRun = activeRun.Complete(
                    0,
                    branchName: null,
                    pullRequestUrl: null,
                    reviewed.Summary,
                    verdict,
                    reviewed.ContainerOutput,
                    recorded);
                await TryDispatchAsync(
                    [new ReviewRunCompletedEvent(
                        activeRun.IssueId.Value,
                        activeRun.Id,
                        recorded.Outcome,
                        recorded.ReviewedCommit,
                        recorded.PatchId,
                        reviewed.Verdict.BlockingFindings,
                        reviewed.Verdict.UnmetCriteria,
                        recorded.Tests.Assessment,
                        timeProvider.GetUtcNow(),
                        reviewed.Verdict.AdvisoryFindings,
                        reviewed.Verdict.UnmetCriterionDetails,
                        recorded.Tests.Reason)],
                    activeRun.Id,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, reviewedRun, alongside, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} reviewed commit {ReviewedCommit}: {Outcome}.",
                    activeRun.Id,
                    recorded.ReviewedCommit,
                    recorded.Outcome);
                break;
            }

            case WorkerOutcome.ContinuableFailure continuable:
            {
                FailedRun continuableFailed = activeRun.Fail(
                    continuable.FailureReason,
                    continuable.BranchName,
                    continuable.ContainerOutput,
                    continuable.Summary,
                    verdict);
                bool continuableProxyPaused = await StagePackageProxyPauseAsync(continuable.FailureReason, cancellationToken);
                await dbContext.TransitionAsync(
                    activeRun,
                    continuableFailed,
                    alongside,
                    domainEventDispatcher,
                    cancellationToken);
                await AnnouncePackageProxyPauseAsync(continuableProxyPaused, cancellationToken);
                await ApplyFailureSideEffectsAsync(activeRun, continuable.FailureReason, cancellationToken);
                logger.LogWarning(
                    "Worker run {WorkerRunId} failed with commits (reason: {Reason}, branch: {BranchName}).",
                    activeRun.Id,
                    continuable.FailureReason.Summary,
                    continuable.BranchName.Value);
                break;
            }

            case WorkerOutcome.Failure failure:
            {
                FailedRun failedRun = activeRun.Fail(
                    failure.FailureReason,
                    branchNameOrNull: null,
                    failure.ContainerOutput,
                    failure.Summary,
                    failure.Verdict ?? verdict);
                bool proxyPaused = await StagePackageProxyPauseAsync(failure.FailureReason, cancellationToken);
                await dbContext.TransitionAsync(activeRun, failedRun, alongside, domainEventDispatcher, cancellationToken);
                await AnnouncePackageProxyPauseAsync(proxyPaused, cancellationToken);
                await ApplyFailureSideEffectsAsync(activeRun, failure.FailureReason, cancellationToken);
                logger.LogWarning(
                    "Worker run {WorkerRunId} failed (reason: {Reason}).",
                    activeRun.Id,
                    failure.FailureReason.Summary);
                break;
            }

            default:
                // WorkerOutcome.Indeterminate must be filtered by the caller before reaching here.
                throw new UnreachableException($"Unhandled outcome type {outcome.GetType().Name}");
        }

    }

    // Only a published run has publish facts; a run resolved straight from its container (a restricted role) has none.
    private static RunPublishFacts? PublishFactsOf(WorkerRun run, string? pullRequestUrl) =>
        run is UnpublishedRun unpublished
            ? new RunPublishFacts(
                unpublished.PublishedHead?.Value,
                pullRequestUrl,
                unpublished.Divergence?.SideBranch.Value,
                unpublished.PublishedBranchCommitCount ?? 0,
                unpublished.Divergence?.Reason)
            : null;

    private static TriageResult ToTriageResult(TriageVerdict verdict) => verdict switch
    {
        TriageVerdict.Ready ready => new TriageResult.Ready(
            ToTriageKind(ready.Kind),
            ready.Decisions,
            ready.Demand,
            ready.DemandReason),
        TriageVerdict.NeedsInput needsInput => new TriageResult.NeedsInput(
            ToTriageKind(needsInput.Kind),
            [.. needsInput.Questions],
            needsInput.Decisions),
        TriageVerdict.Blocked blocked => new TriageResult.Blocked(ToTriageKind(blocked.Kind), blocked.BlockedBy),
        _ => throw new UnreachableException($"Unhandled triage verdict: {verdict.GetType().Name}"),
    };

    private static TriageKind ToTriageKind(TriageIssueKind kind) => kind switch
    {
        TriageIssueKind.Feature => TriageKind.Feature,
        TriageIssueKind.Bug => TriageKind.Bug,
        TriageIssueKind.Refactor => TriageKind.Refactor,
        TriageIssueKind.Documentation => TriageKind.Documentation,
        _ => throw new UnreachableException($"Unhandled triage kind: {kind}"),
    };

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.PackageSourceUnreachable"/>, pauses the package
    /// proxy on the tracked dispatch pause, unsaved, so the pause commits in the run's failure save. Returns <c>true</c> when
    /// this added the pause; a pause already present keeps its backoff.
    /// </summary>
    private async Task<bool> StagePackageProxyPauseAsync(FailureReason failureReason, CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.PackageSourceUnreachable)
        {
            return false;
        }

        DispatchPause? pause = await dbContext.FindDispatchPauseAsync(cancellationToken);

        if (pause is null)
        {
            logger.LogWarning("The package proxy is unreachable, but no dispatch pause row exists to pause it on.");
            return false;
        }

        return pause.PausePackageProxy(timeProvider.GetUtcNow());
    }

    private async Task AnnouncePackageProxyPauseAsync(bool paused, CancellationToken cancellationToken)
    {
        if (!paused)
        {
            return;
        }

        logger.LogWarning(
            "Dispatch is paused for repositories with package sources: the package proxy is unreachable. A probe re-checks it and resumes dispatch.");
        await HostPauseBroadcast.SendAsync(broadcaster, hostPaused: true, logger, cancellationToken);
    }

    private async Task ApplyFailureSideEffectsAsync(
        ITerminableRun activeRun,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        await PersistUsageLimitIfNeededAsync(failureReason, cancellationToken);
        await PublishAuthFailedIfNeededAsync(activeRun, failureReason, cancellationToken);
        await PublishCreditsExhaustedIfNeededAsync(activeRun, failureReason, cancellationToken);
    }

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.UsageLimited"/>,
    /// persists the reset time to <see cref="DispatchPause"/> (extend-only) and dispatches
    /// <see cref="DispatchPausedEvent"/> when the value is newly set or extended.
    /// </summary>
    private async Task PersistUsageLimitIfNeededAsync(
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.UsageLimited usageLimited)
        {
            return;
        }

        DispatchPause? pause = await dbContext.FindDispatchPauseAsync(cancellationToken);

        if (pause is null)
        {
            logger.LogWarning(
                "Usage limit detected but could not persist reset time — no dispatch pause row exists.");
            return;
        }

        DateTimeOffset? resetsAtBefore = pause.UsageLimitResetsAt;
        pause.SetUsageLimitResetsAt(usageLimited.ResetsAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        bool resetsAtChanged = pause.UsageLimitResetsAt != resetsAtBefore;

        if (resetsAtChanged)
        {
            // DispatchPaused has only a SignalR broadcast handler (no durable DB consumer).
            // Route via IIntegrationEventProcessor for direct in-process delivery — no outbox
            // row needed, no extra DB save, and no relay latency on a transient notification.
            await TryDeliverDirectAsync(
                new DispatchPausedEvent(pause.UsageLimitResetsAt!.Value),
                cancellationToken);
        }

        logger.LogWarning(
            "Usage limit detected; dispatch paused until {ResetsAt}.",
            usageLimited.ResetsAt);
    }

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.AuthInvalid"/>,
    /// publishes <see cref="WorkerAuthenticationFailedEvent"/> so the Credentials module
    /// can transition the account state and broadcast the dispatch-paused notification.
    /// </summary>
    private async Task PublishAuthFailedIfNeededAsync(
        ITerminableRun activeRun,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.AuthInvalid)
        {
            return;
        }

        // Enqueue first, then save so the outbox interceptor harvests WorkerAuthenticationFailed
        // atomically. Without this save the event is enqueued into the scoped collector but never
        // drained — the Credentials module's state mutation would be silently lost.
        await TryDispatchSystemEventAsync(
            new WorkerAuthenticationFailedEvent(
                activeRun.Id,
                activeRun.IssueId.Value,
                failureReason.Summary),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Auth-invalid exit detected; WorkerAuthenticationFailed published.");
    }

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.CreditsExhausted"/>,
    /// publishes <see cref="WorkerCreditsExhaustedEvent"/> so the Credentials module can
    /// transition the account spend state and broadcast the dispatch-paused notification.
    /// </summary>
    private async Task PublishCreditsExhaustedIfNeededAsync(
        ITerminableRun activeRun,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.CreditsExhausted)
        {
            return;
        }

        // Enqueue first, then save so the outbox interceptor harvests WorkerCreditsExhausted
        // atomically. Without this save the event is enqueued into the scoped collector but never
        // drained — the Credentials module's state mutation would be silently lost.
        await TryDispatchSystemEventAsync(
            new WorkerCreditsExhaustedEvent(
                activeRun.Id,
                activeRun.IssueId.Value),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Credits-exhausted exit detected; WorkerCreditsExhausted published.");
    }

    private async Task TryDispatchAsync(
        IEnumerable<IIntegrationEvent> events,
        WorkerRunId workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        }
#pragma warning disable CA1031 // Any failure in the handler (e.g. DB error during issue transition) must not crash the BackgroundService tick; the stuck state is visible via the warning log.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to dispatch integration event for WorkerRun {WorkerRunId}", workerRunId);
        }
    }

    /// <summary>
    /// System-level dispatch: the failure message references only the event type rather than
    /// a worker run id.
    /// </summary>
    private async Task TryDispatchSystemEventAsync(
        IIntegrationEvent systemEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventDispatcher.DispatchAsync([systemEvent], cancellationToken);
        }
#pragma warning disable CA1031 // System-level dispatch failures (e.g. DB error in event handler) must not crash the BackgroundService tick; the event type in the warning is sufficient for triage.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to dispatch system integration event {EventType}",
                systemEvent.GetType().Name);
        }
    }

    /// <summary>
    /// Delivers an event directly via <see cref="IIntegrationEventProcessor"/> without writing
    /// an outbox row. Use only for pure ephemeral notifications whose sole consumers are
    /// transient SignalR broadcasts (no durable side-effects). A fresh <see cref="Guid"/> is
    /// used as the event-id because inbox dedup is a no-op for one-shot delivery.
    /// </summary>
    private async Task TryDeliverDirectAsync(
        IIntegrationEvent systemEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventProcessor.ProcessAsync(Guid.NewGuid(), systemEvent, cancellationToken);
        }
#pragma warning disable CA1031 // Direct broadcast delivery failures (e.g. SignalR connection error) must not crash the BackgroundService tick; the warning is sufficient for triage.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to deliver ephemeral integration event {EventType} directly.",
                systemEvent.GetType().Name);
        }
    }

    private async Task TryStopAndRemoveAsync(
        string containerId,
        WorkerRunId workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopAndRemoveAsync(containerId, cancellationToken);
        }
#pragma warning disable CA1031 // Best-effort container removal after a terminal state transition has already succeeded; Docker exceptions must not crash the BackgroundService tick.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to remove container {ContainerId} for WorkerRun {WorkerRunId} after terminal transition.",
                containerId,
                workerRunId);
        }
    }
}
