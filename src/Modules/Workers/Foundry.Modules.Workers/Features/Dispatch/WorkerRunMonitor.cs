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
    RunningBranchCompareGate compareGate,
    IWebhookStatusQueries webhookStatusQueries,
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
        RunningBranchObservation branchObservation = new(compareGate, webhookStatusQueries);

        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(activeRun, timeoutMinutes, cancellationToken, branchObservation: branchObservation);
        }
    }

    private async Task MonitorRunAsync(
        ActiveRun activeRun,
        int timeoutMinutes,
        CancellationToken cancellationToken,
        WorkerStatusProbe? knownProbe = null,
        RunningBranchObservation? branchObservation = null)
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
                await HandleRunningAsync(activeRun, timeoutMinutes, branchObservation, cancellationToken);
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
        RunningBranchObservation? branchObservation,
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

            await ApplyTimeoutOutcomeAsync(activeRun, containerOutput, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} timed out after {TimeoutMinutes} minutes; container stopped.",
                activeRun.Id,
                timeoutMinutes);
            return;
        }

        await ObserveRunningWorkerAsync(activeRun, branchObservation, cancellationToken);
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

    private async Task ObserveRunningWorkerAsync(
        ActiveRun activeRun,
        RunningBranchObservation? branchObservation,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Container is provably alive — the caller has already confirmed this via GetStatusAsync.
        // Record activity unconditionally; do not gate on log growth (claude buffers all output until exit).
        activeRun.RecordActivity(now);

        if (branchObservation is null || await branchObservation.ShouldCompareAsync(activeRun, cancellationToken))
        {
            await RecordBranchCommitsAsync(activeRun, now, cancellationToken);
        }

        if (activeRun.DomainEvents.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await domainEventDispatcher.DispatchAsync(activeRun.DomainEvents, cancellationToken);
            activeRun.ClearDomainEvents();
        }
    }

    private async Task RecordBranchCommitsAsync(
        ActiveRun activeRun,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<BranchCommitSummary> commitResult = await postExitProviderQueries.GetBranchCommitSummaryAsync(
            activeRun.MonitoredRepositoryId,
            activeRun.BranchName.Value,
            cancellationToken);

        switch (commitResult)
        {
            case Result<BranchCommitSummary>.Success { Value: BranchCommitSummary summary }:
                activeRun.RecordBranchCommitCount(summary.CommitCount, summary.LatestSha, now);
                break;

            case Result<BranchCommitSummary>.Failure { Error: { Kind: ErrorKind.NotFound } }:
                // Branch is gone — reset the count to 0 so the UI reflects the deleted branch.
                activeRun.RecordBranchCommitCount(0, null, now);
                break;

            case Result<BranchCommitSummary>.Failure failure:
                // Transient provider error: leave the persisted count unchanged.
                // Do not call RecordBranchCommitCount — the aggregate must not raise a WorkerActivity
                // broadcast for an error state, and the last known count remains accurate.
                logger.LogWarning(
                    "Provider returned a non-NotFound failure for run {WorkerRunId}: {Error}. "
                    + "Leaving persisted commit count unchanged.",
                    activeRun.Id,
                    failure.Error);
                break;
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

    /// <summary>
    /// Per-pass view of the running-branch compare policy (issue #660): skips the compare for a run whose
    /// repository has an <c>Active</c> webhook unless a push is pending, so push webhooks replace polling.
    /// </summary>
    private sealed class RunningBranchObservation(
        RunningBranchCompareGate gate,
        IWebhookStatusQueries webhookStatusQueries)
    {
        private readonly Dictionary<MonitoredRepositoryId, bool> _webhookActive = [];

        public async Task<bool> ShouldCompareAsync(ActiveRun run, CancellationToken cancellationToken)
        {
            if (!_webhookActive.TryGetValue(run.MonitoredRepositoryId, out bool active))
            {
                active = await webhookStatusQueries.IsWebhookActiveAsync(run.MonitoredRepositoryId, cancellationToken);
                _webhookActive[run.MonitoredRepositoryId] = active;
            }

            return gate.ShouldCompare(run.Id, run.MonitoredRepositoryId, run.BranchName.Value, active);
        }
    }
}
