using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Modules.Workers.Features.Verdicts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Monitors active worker runs: probes each container, resolves exited, missing, timed-out
/// and cancel-requested runs to a <see cref="WorkerOutcome"/> (applied via
/// <see cref="WorkerOutcomeApplier"/>), and records activity for still-running workers.
/// </summary>
internal sealed class WorkerRunMonitor(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IDomainEventDispatcher domainEventDispatcher,
    WorkerOutcomeResolver resolver,
    WorkerOutcomeApplier outcomeApplier,
    IIntegrationEventDispatcher integrationEventDispatcher,
    IPostExitProviderQueries postExitProviderQueries,
    IContainerOutputParser containerOutputParser,
    RunVerdictReader verdictReader,
    TimeProvider timeProvider,
    ILogger<WorkerRunMonitor> logger)
{
    private const int LogTailLines = 500;
    private const int MaxContainerOutputLength = 65_536;

    /// <summary>
    /// Resolves runs whose containers are missing or have already exited, removing them from
    /// <paramref name="activeRuns"/> so the monitoring pass does not process them twice.
    /// Returns <c>false</c> when the Docker daemon was unreachable for any run.
    /// </summary>
    public async Task<bool> ReconcileOrphanedRunsAsync(
        List<ActiveRun> activeRuns,
        int timeoutMinutes,
        CancellationToken cancellationToken)
    {
        // Orphaned container reaping is now owned by StaleStartingRunService.
        // Reachability is determined solely by the per-run GetStatusAsync probe below.
        bool daemonReachable = true;
        List<ActiveRun> runsToRemove = [];

        foreach (ActiveRun activeRun in activeRuns)
        {
            WorkerStatusProbe probe = await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

            switch (probe)
            {
                case WorkerStatusProbe.Unreachable:
                    LogDaemonUnreachable(activeRun.Id, activeRun.ContainerId.Value);
                    daemonReachable = false;
                    continue;

                case WorkerStatusProbe.NotFound:
                {
                    if (activeRun.BranchName is not null)
                    {
                        await BeginPublishingAsync(activeRun, ExitOf(status: null, RunStopReason.Exited), containerOutput: null, verdict: null, cancellationToken, containerGone: true);
                        runsToRemove.Add(activeRun);
                        break;
                    }

                    WorkerOutcome outcome = await resolver.ResolveAsync(
                        activeRun,
                        status: null,
                        containerOutput: null,
                        cancellationToken);

                    if (outcome is WorkerOutcome.Indeterminate indeterminate)
                    {
                        logger.LogWarning(
                            "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation; "
                            + "state indeterminate: {Error}. Leaving for next tick.",
                            activeRun.Id,
                            activeRun.ContainerId.Value,
                            indeterminate.Error);
                        continue;
                    }

                    runsToRemove.Add(activeRun);

                    await outcomeApplier.ApplyAsync(outcome, activeRun, cancellationToken);

                    logger.LogWarning(
                        "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation.",
                        activeRun.Id,
                        activeRun.ContainerId.Value);
                    break;
                }

                case WorkerStatusProbe.Available available when !available.Status.IsRunning:
                    await MonitorRunAsync(activeRun, timeoutMinutes, cancellationToken, knownProbe: probe);
                    runsToRemove.Add(activeRun);
                    break;

                case WorkerStatusProbe.Available:
                    // Container is still running — leave in active list for monitoring loop.
                    break;

                default:
                    throw new UnreachableException($"Unhandled WorkerStatusProbe type {probe.GetType().Name}");
            }
        }

        foreach (ActiveRun run in runsToRemove)
        {
            activeRuns.Remove(run);
        }

        return daemonReachable;
    }

    public async Task MonitorActiveRunsAsync(
        IReadOnlyList<ActiveRun> activeRuns,
        int timeoutMinutes,
        CancellationToken cancellationToken)
    {
        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(activeRun, timeoutMinutes, cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        ActiveRun activeRun,
        int timeoutMinutes,
        CancellationToken cancellationToken,
        WorkerStatusProbe? knownProbe = null)
    {
        WorkerStatusProbe statusProbe = knownProbe
            ?? await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

        switch (statusProbe)
        {
            case WorkerStatusProbe.Unreachable:
                LogDaemonUnreachable(activeRun.Id, activeRun.ContainerId.Value);
                return;

            case WorkerStatusProbe.NotFound:
                await HandleNotFoundAsync(activeRun, cancellationToken);
                return;

            case WorkerStatusProbe.Available { Status.IsRunning: true }:
                await HandleRunningAsync(activeRun, timeoutMinutes, cancellationToken);
                return;

            case WorkerStatusProbe.Available available:
                await HandleExitedAsync(activeRun, available.Status, timeoutMinutes, cancellationToken);
                return;

            default:
                throw new UnreachableException($"Unhandled WorkerStatusProbe type {statusProbe.GetType().Name}");
        }
    }

    private async Task HandleNotFoundAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        if (activeRun.BranchName is not null)
        {
            await BeginPublishingAsync(activeRun, ExitOf(status: null, RunStopReason.Exited), containerOutput: null, verdict: null, cancellationToken, containerGone: true);
            return;
        }

        WorkerOutcome outcome = await resolver.ResolveAsync(
            activeRun,
            status: null,
            containerOutput: null,
            cancellationToken);

        if (outcome is WorkerOutcome.Indeterminate indeterminate)
        {
            logger.LogWarning(
                "Worker run {WorkerRunId} container {ContainerId} not found; "
                + "state indeterminate: {Error}. Leaving for next tick.",
                activeRun.Id,
                activeRun.ContainerId.Value,
                indeterminate.Error);
            return;
        }

        await outcomeApplier.ApplyAsync(outcome, activeRun, cancellationToken);

        logger.LogWarning(
            "Worker run {WorkerRunId} container {ContainerId} not found; marked as {OutcomeType}.",
            activeRun.Id,
            activeRun.ContainerId.Value,
            outcome.GetType().Name);
    }

    private async Task HandleRunningAsync(
        ActiveRun activeRun,
        int timeoutMinutes,
        CancellationToken cancellationToken)
    {
        // A pending cancel request beats the timeout. Only a still-running container is
        // cancelled: one that already exited resolves to its real outcome instead.
        if (activeRun.CancelRequestedAt.HasValue)
        {
            await CancelRunningWorkerAsync(activeRun, cancellationToken);
            return;
        }

        // Timeout applies only to still-running containers — an exited container's MR state
        // must be consulted first regardless of wall-clock time.
        if (DateTimeOffset.UtcNow >= activeRun.StartedAt.AddMinutes(timeoutMinutes))
        {
            await TryStopContainerAsync(activeRun, cancellationToken);

            string? containerOutput = await TryGetLogsAsync(activeRun, cancellationToken);

            if (activeRun.BranchName is not null)
            {
                await BeginPublishingAsync(activeRun, ExitOf(status: null, RunStopReason.TimedOut), containerOutput, verdict: null, cancellationToken);
            }
            else
            {
                await ApplyTimeoutOutcomeAsync(activeRun, containerOutput, cancellationToken);
            }

            logger.LogWarning(
                "Worker run {WorkerRunId} timed out after {TimeoutMinutes} minutes; container stopped.",
                activeRun.Id,
                timeoutMinutes);
            return;
        }

        await ObserveRunningWorkerAsync(activeRun, cancellationToken);
    }

    private async Task HandleExitedAsync(
        ActiveRun activeRun,
        WorkerStatus status,
        int timeoutMinutes,
        CancellationToken cancellationToken)
    {
        // Container has exited — always resolve via MR-state-first so a merged PR produces
        // Completed even when the wall-clock timeout has passed.
        string? containerOutput = await TryGetLogsAsync(activeRun, cancellationToken);

        // Only a run that exited 0 finished its turn and can carry a verdict; any other exit keeps
        // today's failure classification. A needs_input verdict moves the issue to awaiting input (ADR 0096).
        RunVerdict? verdict = status.ExitCode == 0
            ? verdictReader.Read(activeRun.Role, containerOutput)
            : null;

        // A run with a branch publishes its commits first (ADR 0100); its outcome is resolved once they are on the branch.
        if (activeRun.BranchName is not null)
        {
            await BeginPublishingAsync(activeRun, ExitOf(status, RunStopReason.Exited), containerOutput, verdict, cancellationToken);
            return;
        }

        WorkerOutcome outcome = await resolver.ResolveAsync(
            activeRun,
            status,
            containerOutput,
            verdict,
            cancellationToken);

        if (outcome is WorkerOutcome.Indeterminate indeterminate)
        {
            // Resolver could not determine the outcome (transient provider error).
            // If the run has also exceeded the timeout ceiling, force a timeout outcome so
            // the slot is not held indefinitely; otherwise leave for the next tick.
            if (DateTimeOffset.UtcNow >= activeRun.StartedAt.AddMinutes(timeoutMinutes))
            {
                await ApplyTimeoutOutcomeAsync(activeRun, containerOutput, cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} exited but state is indeterminate and timeout has elapsed; "
                    + "forcing timeout outcome: {Error}.",
                    activeRun.Id,
                    indeterminate.Error);
                return;
            }

            logger.LogWarning(
                "Worker run {WorkerRunId} exited but state is indeterminate: {Error}. Leaving for next tick.",
                activeRun.Id,
                indeterminate.Error);
            return;
        }

        WorkerRunTranscript? transcript = await TryCaptureTranscriptAsync(activeRun, cancellationToken);

        await outcomeApplier.ApplyAsync(outcome, activeRun, verdict, transcript, cancellationToken);
    }

    private async Task ApplyTimeoutOutcomeAsync(
        ActiveRun activeRun,
        string? containerOutput,
        CancellationToken cancellationToken)
    {
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        WorkerOutcome timeoutOutcome = await StoppedRunOutcomeBuilder.BuildAsync(
            activeRun,
            new FailureReason.TimedOut(),
            containerOutput,
            summary,
            postExitProviderQueries,
            cancellationToken);

        WorkerRunTranscript? transcript = await TryCaptureTranscriptAsync(activeRun, cancellationToken);

        await outcomeApplier.ApplyAsync(timeoutOutcome, activeRun, verdict: null, transcript, cancellationToken);
    }

    /// <summary>
    /// Applies a user's cancel request to a still-running worker, all within one tick:
    /// stop the container, capture its logs, then check the provider for a merge request.
    /// An open or merged merge request means the worker finished its job before the cancel took
    /// effect, so the run resolves normally. Otherwise the run fails with
    /// <see cref="FailureReason.Cancelled"/>, keeping the branch when it has commits.
    /// </summary>
    private async Task CancelRunningWorkerAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        await TryStopContainerAsync(activeRun, cancellationToken);

        string? containerOutput = await TryGetLogsAsync(activeRun, cancellationToken);

        if (activeRun.BranchName is not null)
        {
            // On SIGTERM the worker committed and bundled its work; Foundry publishes it, so Retry continues from it.
            await BeginPublishingAsync(activeRun, ExitOf(status: null, RunStopReason.Cancelled), containerOutput, verdict: null, cancellationToken);
            logger.LogWarning(
                "Worker run {WorkerRunId} cancel requested at {CancelRequestedAt}; container stopped and its commits are being published.",
                activeRun.Id,
                activeRun.CancelRequestedAt);
            return;
        }

        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        // A transient merge-request lookup failure is treated as "no PR" (accepted risk): the run may
        // land in a retryable failed state while a PR is open.
        WorkerOutcome outcome = await resolver.TryResolveExistingPullRequestAsync(activeRun, containerOutput, summary, cancellationToken)
            ?? await StoppedRunOutcomeBuilder.BuildAsync(
                activeRun,
                new FailureReason.Cancelled(),
                containerOutput,
                summary,
                postExitProviderQueries,
                cancellationToken);

        WorkerRunTranscript? transcript = await TryCaptureTranscriptAsync(activeRun, cancellationToken);

        await outcomeApplier.ApplyAsync(outcome, activeRun, verdict: null, transcript, cancellationToken);

        logger.LogWarning(
            "Worker run {WorkerRunId} cancel requested at {CancelRequestedAt}; container stopped and run marked as {OutcomeType}.",
            activeRun.Id,
            activeRun.CancelRequestedAt,
            outcome.GetType().Name);
    }

    private async Task ObserveRunningWorkerAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Container is provably alive — the caller has already confirmed this via GetStatusAsync.
        // Record activity unconditionally; do not gate on log growth (claude buffers all output until exit).
        activeRun.RecordActivity(now);

        if (activeRun.DomainEvents.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await domainEventDispatcher.DispatchAsync(activeRun.DomainEvents, cancellationToken);
            activeRun.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Moves a run whose container ended into publishing (ADR 0100): its transcript and exit facts are kept on the run,
    /// and its container is removed while the Hand-off volume with the Run bundle stays for the Publisher.
    /// </summary>
    private async Task BeginPublishingAsync(
        ActiveRun activeRun,
        RunExit exit,
        string? containerOutput,
        RunVerdict? verdict,
        CancellationToken cancellationToken,
        bool containerGone = false)
    {
        // A container that is gone has no log left to render.
        WorkerRunTranscript? transcript = containerGone ? null : await TryCaptureTranscriptAsync(activeRun, cancellationToken);

        RunPullRequestText? pullRequestText = exit.FinishedCleanly
            ? RunPullRequestTextReader.Read(containerOutputParser.ParseStructuredOutput(containerOutput))
            : null;

        PublishingRun publishing = activeRun.BeginPublishing(exit, containerOutput, verdict, pullRequestText, transcript);

        // Enqueued before the transition so the outbox harvests it atomically with the state change. It only refreshes the
        // issue list, so a failed dispatch must not hold the run in Running.
        try
        {
            await integrationEventDispatcher.DispatchAsync(
                [new WorkerRunPublishPhaseChanged(activeRun.Id, activeRun.IssueId.Value, WorkerRunPublishPhase.Publishing)],
                cancellationToken);
        }
#pragma warning disable CA1031 // The phase change only refreshes the dashboard; the transition must still happen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to dispatch the publish phase change for WorkerRun {WorkerRunId}.", activeRun.Id);
        }
        await dbContext.TransitionAsync(activeRun, publishing, domainEventDispatcher, cancellationToken);

        await TryRemoveContainerKeepingHandOffAsync(activeRun, cancellationToken);

        logger.LogInformation(
            "Worker run {WorkerRunId} ended ({StopReason}, exit code {ExitCode}); publishing its commits.",
            activeRun.Id,
            exit.StopReason,
            exit.ExitCode);
    }

    private RunExit ExitOf(WorkerStatus? status, RunStopReason stopReason) =>
        new(status?.ExitCode, status?.OomKilled ?? false, stopReason, timeProvider.GetUtcNow());

    private async Task TryRemoveContainerKeepingHandOffAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopAndRemoveKeepingHandOffAsync(activeRun.ContainerId.Value, cancellationToken);
        }
#pragma warning disable CA1031 // Best-effort removal after the run moved to publishing; the orphaned-container reaper removes a leftover.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to remove container {ContainerId} for WorkerRun {WorkerRunId} after it moved to publishing.",
                activeRun.ContainerId.Value,
                activeRun.Id);
        }
    }

    private async Task TryStopContainerAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopContainerAsync(activeRun.ContainerId.Value, cancellationToken);
        }
#pragma warning disable CA1031 // Best-effort stop before log capture on timeout/cancel paths; Docker exceptions must not crash the BackgroundService tick or prevent log capture and cleanup.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to stop container {ContainerId} for WorkerRun {WorkerRunId}.",
                activeRun.ContainerId.Value,
                activeRun.Id);
        }
    }

    private async Task<string?> TryGetLogsAsync(ActiveRun activeRun, CancellationToken cancellationToken)
    {
        try
        {
            string? output = await orchestrator.GetLogsAsync(activeRun.ContainerId.Value, LogTailLines, cancellationToken);

            if (output is null)
            {
                return null;
            }

            // Keep the tail: stream-json output ends with the result event that outcome
            // classification and the run summary are parsed from.
            return output.Length > MaxContainerOutputLength
                ? output[^MaxContainerOutputLength..]
                : output;
        }
#pragma warning disable CA1031 // Best-effort log capture before transition; Docker exceptions must not crash the BackgroundService tick or prevent the failure transition.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to capture logs for container {ContainerId} (WorkerRun {WorkerRunId}).",
                activeRun.ContainerId.Value,
                activeRun.Id);
            return null;
        }
    }

    /// <summary>
    /// Renders the container's full log as the run's Run Transcript, read before the applier removes the
    /// container. Best-effort: a failed fetch or render yields no transcript and leaves the outcome as is.
    /// </summary>
    private async Task<WorkerRunTranscript?> TryCaptureTranscriptAsync(
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        try
        {
            string? fullLog = await orchestrator.GetFullLogAsync(activeRun.ContainerId.Value, cancellationToken);

            if (string.IsNullOrWhiteSpace(fullLog))
            {
                return null;
            }

            string rendered = AgentTranscriptFormatter.FormatLog(fullLog);

            return WorkerRunTranscript.Capture(activeRun.Id, rendered, timeProvider.GetUtcNow());
        }
#pragma warning disable CA1031 // Best-effort transcript capture before transition; a Docker or rendering failure must not prevent the terminal transition.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to capture the run transcript for container {ContainerId} (WorkerRun {WorkerRunId}).",
                activeRun.ContainerId.Value,
                activeRun.Id);
            return null;
        }
    }

    private void LogDaemonUnreachable(WorkerRunId runId, string containerId)
    {
        logger.LogWarning(
            "Docker daemon unreachable while monitoring worker run {WorkerRunId} container {ContainerId}; "
            + "deferring until next tick.",
            runId,
            containerId);
    }
}
