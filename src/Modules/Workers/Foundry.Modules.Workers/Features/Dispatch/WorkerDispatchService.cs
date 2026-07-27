using System.Diagnostics;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using DispatchPausedEvent = Foundry.Modules.Workers.Contracts.DispatchPaused;
using DispatchResumedEvent = Foundry.Modules.Workers.Contracts.DispatchResumed;

using WorkerAuthenticationFailedEvent = Foundry.Modules.Workers.Contracts.WorkerAuthenticationFailed;
using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal sealed class WorkerDispatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerDispatchService> logger) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    // Safe without locking — PeriodicTimer loop is single-threaded
    private bool _reconciled;

    // Per-run in-memory tick state for activity and commit detection.
    // Keyed by WorkerRunId; entries are added on first observation and removed on terminal transition.
    private readonly Dictionary<WorkerRunId, int> _lastSeenLogLength = [];
    private readonly Dictionary<WorkerRunId, string> _lastSeenCommitSha = [];

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IWorkerOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IWorkerOrchestrator>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        // Bridge handlers (WorkerRunFailedBridgeHandler) enqueue integration events inside the
        // TransitionAsync transaction via the outbox interceptor. Using the raw dispatcher means
        // a bridge-handler throw rolls back the entire transaction (state + outbox row) atomically.
        // In production, OutboxIntegrationEventDispatcher only enqueues into a list and never throws,
        // so the bridge handler path is safe without any fault-tolerant wrapper.
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        IIntegrationEventProcessor integrationEventProcessor =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
        IGlobalSettingsQueries settingsQueries =
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        IPostExitProviderQueries postExitProviderQueries =
            scope.ServiceProvider.GetRequiredService<IPostExitProviderQueries>();
        IContainerOutputParser containerOutputParser =
            scope.ServiceProvider.GetRequiredService<IContainerOutputParser>();
        WorkerOutcomeResolver resolver = scope.ServiceProvider.GetRequiredService<WorkerOutcomeResolver>();
        ICredentialGate credentialGate =
            scope.ServiceProvider.GetRequiredService<ICredentialGate>();

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);
        int defaultCooldownMinutes = await settingsQueries.GetDefaultCooldownMinutesAsync(cancellationToken);

        if (!_reconciled)
        {
            _reconciled = await ReconcileOrphanedRunsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                integrationEventProcessor,
                domainEventDispatcher,
                resolver,
                postExitProviderQueries,
                containerOutputParser,
                timeoutMinutes,
                defaultCooldownMinutes,
                activeRuns,
                cancellationToken);
        }

        await MonitorActiveRunsAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
            integrationEventProcessor,
            domainEventDispatcher,
            resolver,
            postExitProviderQueries,
            containerOutputParser,
            timeoutMinutes,
            defaultCooldownMinutes,
            activeRuns,
            cancellationToken);

        DispatchPauseState pauseState = await settingsQueries.GetDispatchPauseStateAsync(cancellationToken);

        bool autoResumed = await TryAutoResumeAsync(
            dbContext,
            integrationEventDispatcher,
            pauseState,
            cancellationToken);

        bool canDispatch = await credentialGate.CanDispatchAsync(cancellationToken);

        if (!canDispatch)
        {
            logger.LogDebug("Dispatch skipped: credential gate returned false.");
            return;
        }

        if (!autoResumed && (pauseState.IsDispatchPaused || pauseState.UsageLimitResetsAt.HasValue))
        {
            logger.LogDebug(
                "Dispatch skipped: dispatch is paused (IsDispatchPaused={IsDispatchPaused}, UsageLimitResetsAt={UsageLimitResetsAt}).",
                pauseState.IsDispatchPaused,
                pauseState.UsageLimitResetsAt);
            return;
        }

        ImageBuildStatus imageBuildStatus = await settingsQueries.GetImageBuildStatusAsync(cancellationToken);

        if (imageBuildStatus is ImageBuildStatus.Building or ImageBuildStatus.Failed)
        {
            logger.LogDebug(
                "Dispatch skipped: worker image build is not ready (ImageBuildStatus={ImageBuildStatus}).",
                imageBuildStatus);
            return;
        }

        int activeCount = activeRuns.Count;
        int maxConcurrent = await settingsQueries.GetMaxConcurrentAsync(cancellationToken);

        if (activeCount >= maxConcurrent)
        {
            logger.LogDebug(
                "Dispatch skipped: {ActiveCount}/{MaxConcurrent} slots in use.",
                activeCount,
                maxConcurrent);
            return;
        }

        Guid workerRunId = Guid.NewGuid();
        await TryDispatchAsync(
            integrationEventDispatcher,
            [new WorkerCapacityAvailable(workerRunId)],
            workerRunId,
            cancellationToken);

        // Harvest: enqueue-before-save ensures WorkerCapacityAvailable is written atomically
        // to outbox_messages. Without this save the scoped collector holds the event but it is
        // never drained and the IssueClaimed downstream transition is silently lost.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Exposed as <c>internal</c> for unit tests that need to verify the reachability signal
    /// returned by <see cref="ReconcileOrphanedRunsAsync"/> before step 7 wires the gate.
    /// </summary>
    internal async Task<bool> ReconcileOrphanedRunsForTestAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IWorkerOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IWorkerOrchestrator>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        IIntegrationEventProcessor integrationEventProcessor =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
        IGlobalSettingsQueries settingsQueries =
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        IPostExitProviderQueries postExitProviderQueries =
            scope.ServiceProvider.GetRequiredService<IPostExitProviderQueries>();
        IContainerOutputParser containerOutputParser =
            scope.ServiceProvider.GetRequiredService<IContainerOutputParser>();
        WorkerOutcomeResolver resolver = scope.ServiceProvider.GetRequiredService<WorkerOutcomeResolver>();

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);
        int defaultCooldownMinutes = await settingsQueries.GetDefaultCooldownMinutesAsync(cancellationToken);

        return await ReconcileOrphanedRunsAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
            integrationEventProcessor,
            domainEventDispatcher,
            resolver,
            postExitProviderQueries,
            containerOutputParser,
            timeoutMinutes,
            defaultCooldownMinutes,
            activeRuns,
            cancellationToken);
    }

    private async Task<bool> ReconcileOrphanedRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IIntegrationEventProcessor integrationEventProcessor,
        IDomainEventDispatcher domainEventDispatcher,
        WorkerOutcomeResolver resolver,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        bool daemonReachable = await RemoveUnknownContainersAsync(dbContext, orchestrator, cancellationToken);
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
                        exitCode: null,
                        containerOutput: null,
                        defaultCooldownMinutes,
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

                    await ApplyOutcomeAsync(
                        outcome,
                        activeRun,
                        dbContext,
                        orchestrator,
                        integrationEventDispatcher,
                        integrationEventProcessor,
                        domainEventDispatcher,
                        cancellationToken);

                    logger.LogWarning(
                        "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation.",
                        activeRun.Id,
                        activeRun.ContainerId.Value);
                    break;
                }

                case WorkerStatusProbe.Available available when !available.Status.IsRunning:
                    await MonitorRunAsync(
                        dbContext,
                        orchestrator,
                        integrationEventDispatcher,
                        integrationEventProcessor,
                        domainEventDispatcher,
                        resolver,
                        postExitProviderQueries,
                        containerOutputParser,
                        timeoutMinutes,
                        defaultCooldownMinutes,
                        activeRun,
                        cancellationToken,
                        knownProbe: probe);
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

    private async Task MonitorActiveRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IIntegrationEventProcessor integrationEventProcessor,
        IDomainEventDispatcher domainEventDispatcher,
        WorkerOutcomeResolver resolver,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                integrationEventProcessor,
                domainEventDispatcher,
                resolver,
                postExitProviderQueries,
                containerOutputParser,
                timeoutMinutes,
                defaultCooldownMinutes,
                activeRun,
                cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IIntegrationEventProcessor integrationEventProcessor,
        IDomainEventDispatcher domainEventDispatcher,
        WorkerOutcomeResolver resolver,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
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
            {
                WorkerOutcome outcome = await resolver.ResolveAsync(
                    activeRun,
                    exitCode: null,
                    containerOutput: null,
                    defaultCooldownMinutes,
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

                await ApplyOutcomeAsync(
                    outcome,
                    activeRun,
                    dbContext,
                    orchestrator,
                    integrationEventDispatcher,
                    integrationEventProcessor,
                    domainEventDispatcher,
                    cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} container {ContainerId} not found; marked as {OutcomeType}.",
                    activeRun.Id,
                    activeRun.ContainerId.Value,
                    outcome.GetType().Name);
                return;
            }

            case WorkerStatusProbe.Available available:
            {
                WorkerStatus status = available.Status;
                DateTimeOffset timeout = activeRun.StartedAt.AddMinutes(timeoutMinutes);

                if (status.IsRunning)
                {
                    // Timeout applies only to still-running containers — an exited container's MR state
                    // must be consulted first regardless of wall-clock time.
                    if (DateTimeOffset.UtcNow >= timeout)
                    {
                        await TryStopContainerAsync(
                            orchestrator,
                            activeRun.ContainerId.Value,
                            activeRun.Id.Value,
                            cancellationToken);

                        string? containerOutput = await TryGetLogsAsync(
                            orchestrator,
                            activeRun.ContainerId.Value,
                            activeRun.Id.Value,
                            cancellationToken);

                        RunResultSummary? timeoutSummary = containerOutputParser.ParseRunResultSummary(containerOutput);

                        WorkerOutcome timeoutOutcome = await BuildTimeoutOutcomeAsync(
                            activeRun,
                            containerOutput,
                            timeoutSummary,
                            postExitProviderQueries,
                            cancellationToken);

                        await ApplyOutcomeAsync(
                            timeoutOutcome,
                            activeRun,
                            dbContext,
                            orchestrator,
                            integrationEventDispatcher,
                            integrationEventProcessor,
                            domainEventDispatcher,
                            cancellationToken);

                        logger.LogWarning(
                            "Worker run {WorkerRunId} timed out after {TimeoutMinutes} minutes; container stopped.",
                            activeRun.Id,
                            timeoutMinutes);
                        return;
                    }

                    await ObserveRunningWorkerAsync(
                        dbContext,
                        orchestrator,
                        domainEventDispatcher,
                        postExitProviderQueries,
                        activeRun,
                        cancellationToken);
                    return;
                }

                // Container has exited — always resolve via MR-state-first so a merged PR produces
                // Completed even when the wall-clock timeout has passed.
                string? exitContainerOutput = await TryGetLogsAsync(
                    orchestrator,
                    activeRun.ContainerId.Value,
                    activeRun.Id.Value,
                    cancellationToken);

                WorkerOutcome exitOutcome = await resolver.ResolveAsync(
                    activeRun,
                    status.ExitCode,
                    exitContainerOutput,
                    defaultCooldownMinutes,
                    cancellationToken);

                if (exitOutcome is WorkerOutcome.Indeterminate exitIndeterminate)
                {
                    // Resolver could not determine the outcome (transient provider error).
                    // If the run has also exceeded the timeout ceiling, force a timeout outcome so
                    // the slot is not held indefinitely; otherwise leave for the next tick.
                    if (DateTimeOffset.UtcNow >= timeout)
                    {
                        RunResultSummary? timeoutSummary = containerOutputParser.ParseRunResultSummary(exitContainerOutput);
                        WorkerOutcome timeoutOutcome = await BuildTimeoutOutcomeAsync(
                            activeRun,
                            exitContainerOutput,
                            timeoutSummary,
                            postExitProviderQueries,
                            cancellationToken);

                        await ApplyOutcomeAsync(
                            timeoutOutcome,
                            activeRun,
                            dbContext,
                            orchestrator,
                            integrationEventDispatcher,
                            integrationEventProcessor,
                            domainEventDispatcher,
                            cancellationToken);

                        logger.LogWarning(
                            "Worker run {WorkerRunId} exited but state is indeterminate and timeout has elapsed; "
                            + "forcing timeout outcome: {Error}.",
                            activeRun.Id,
                            exitIndeterminate.Error);
                        return;
                    }

                    logger.LogWarning(
                        "Worker run {WorkerRunId} exited but state is indeterminate: {Error}. Leaving for next tick.",
                        activeRun.Id,
                        exitIndeterminate.Error);
                    return;
                }

                await ApplyOutcomeAsync(
                    exitOutcome,
                    activeRun,
                    dbContext,
                    orchestrator,
                    integrationEventDispatcher,
                    integrationEventProcessor,
                    domainEventDispatcher,
                    cancellationToken);
                return;
            }

            default:
                throw new UnreachableException($"Unhandled WorkerStatusProbe type {statusProbe.GetType().Name}");
        }
    }

    /// <summary>
    /// Applies side effects for a resolved <see cref="WorkerOutcome"/>: state transition,
    /// integration event dispatch, usage-limit persistence, and container stop+remove.
    /// <see cref="WorkerOutcome.Indeterminate"/> must be handled by the caller before
    /// invoking this method.
    /// </summary>
    private async Task ApplyOutcomeAsync(
        WorkerOutcome outcome,
        ActiveRun activeRun,
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IIntegrationEventProcessor integrationEventProcessor,
        IDomainEventDispatcher domainEventDispatcher,
        CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case WorkerOutcome.Completed completed:
            {
                CompletedRun completedRun = activeRun.Complete(
                    0,
                    completed.BranchName,
                    completed.PullRequestUrl,
                    completed.Summary);
                // Enqueue before TransitionAsync: the trailing harvest SaveChangesAsync inside
                // TransitionAsync drains the outbox collector atomically with the state change.
                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunCompletedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        completed.BranchName.Value,
                        completed.PullRequestUrl.Value,
                        WorkerRunMergeState.Merged)],
                    activeRun.Id.Value,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, completedRun, domainEventDispatcher, cancellationToken);
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
                    review.Summary);
                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunCompletedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        review.BranchName.Value,
                        review.PullRequestUrl.Value,
                        WorkerRunMergeState.Open)],
                    activeRun.Id.Value,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, reviewRun, domainEventDispatcher, cancellationToken);
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
                    unchanged.Summary);
                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunCompletedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        unchanged.BranchName.Value,
                        null,
                        WorkerRunMergeState.None)],
                    activeRun.Id.Value,
                    cancellationToken);
                await dbContext.TransitionAsync(activeRun, unchangedRun, domainEventDispatcher, cancellationToken);
                logger.LogInformation(
                    "Worker run {WorkerRunId} completed with no commits (unchanged, branch: {BranchName}).",
                    activeRun.Id,
                    unchanged.BranchName.Value);
                break;
            }

            case WorkerOutcome.ContinuableFailure continuable:
            {
                FailedRun continuableFailed = activeRun.Fail(
                    continuable.FailureReason,
                    continuable.BranchName,
                    continuable.ContainerOutput,
                    continuable.Summary);
                await dbContext.TransitionAsync(
                    activeRun,
                    continuableFailed,
                    domainEventDispatcher,
                    cancellationToken);
                await PersistUsageLimitIfNeededAsync(
                    dbContext,
                    integrationEventProcessor,
                    continuable.FailureReason,
                    cancellationToken);
                await PublishAuthFailedIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    activeRun,
                    continuable.FailureReason,
                    cancellationToken);
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
                    failure.Summary);
                await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);
                await PersistUsageLimitIfNeededAsync(
                    dbContext,
                    integrationEventProcessor,
                    failure.FailureReason,
                    cancellationToken);
                await PublishAuthFailedIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    activeRun,
                    failure.FailureReason,
                    cancellationToken);
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

        _lastSeenLogLength.Remove(activeRun.Id);
        _lastSeenCommitSha.Remove(activeRun.Id);
        await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
    }

    /// <summary>
    /// Builds a <see cref="WorkerOutcome"/> for a run that exceeded the timeout ceiling.
    /// Consults <see cref="IPostExitProviderQueries.HasBranchCommitsAsync"/> to decide between
    /// <see cref="WorkerOutcome.ContinuableFailure"/> (commits exist or query failed transiently)
    /// and <see cref="WorkerOutcome.Failure"/> (definitively no commits).
    /// </summary>
    private static async Task<WorkerOutcome> BuildTimeoutOutcomeAsync(
        ActiveRun run,
        string? containerOutput,
        RunResultSummary? summary,
        IPostExitProviderQueries postExitProviderQueries,
        CancellationToken cancellationToken)
    {
        Result<bool> commitsResult = await postExitProviderQueries.HasBranchCommitsAsync(
            run.MonitoredRepositoryId,
            run.BranchName.Value,
            cancellationToken);

        FailureReason timedOut = new FailureReason.TimedOut();

        if (commitsResult is Result<bool>.Failure commitsFailure)
        {
            if (commitsFailure.Error.Kind == ErrorKind.NotFound)
            {
                // Branch definitively does not exist — no commits to preserve
                return new WorkerOutcome.Failure(timedOut, containerOutput, summary);
            }

            // Transient provider failure — preserve the branch to allow a future retry
            return new WorkerOutcome.ContinuableFailure(run.BranchName, timedOut, containerOutput, summary);
        }

        bool hasCommits = ((Result<bool>.Success)commitsResult).Value;

        return hasCommits
            ? new WorkerOutcome.ContinuableFailure(run.BranchName, timedOut, containerOutput, summary)
            : new WorkerOutcome.Failure(timedOut, containerOutput, summary);
    }

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.UsageLimited"/>,
    /// persists the reset time to <see cref="GlobalSettings"/> (extend-only) and dispatches
    /// <see cref="DispatchPausedEvent"/> when the value is newly set or extended.
    /// </summary>
    private async Task PersistUsageLimitIfNeededAsync(
        DbContext dbContext,
        IIntegrationEventProcessor integrationEventProcessor,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.UsageLimited usageLimited)
        {
            return;
        }

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Usage limit detected but could not persist reset time — no GlobalSettings row exists.");
            return;
        }

        DateTimeOffset? resetsAtBefore = settings.UsageLimitResetsAt;
        settings.SetUsageLimitResetsAt(usageLimited.ResetsAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        bool resetsAtChanged = settings.UsageLimitResetsAt != resetsAtBefore;

        if (resetsAtChanged)
        {
            // DispatchPaused has only a SignalR broadcast handler (no durable DB consumer).
            // Route via IIntegrationEventProcessor for direct in-process delivery — no outbox
            // row needed, no extra DB save, and no relay latency on a transient notification.
            await TryDeliverDirectAsync(
                integrationEventProcessor,
                new DispatchPausedEvent(settings.UsageLimitResetsAt!.Value),
                cancellationToken);
        }

        logger.LogWarning(
            "Usage limit detected; dispatch paused until {ResetsAt}.",
            usageLimited.ResetsAt);
    }

    private async Task ObserveRunningWorkerAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IDomainEventDispatcher domainEventDispatcher,
        IPostExitProviderQueries postExitProviderQueries,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string? currentLogs = await TryGetLogsAsync(
            orchestrator,
            activeRun.ContainerId.Value,
            activeRun.Id.Value,
            cancellationToken);

        int currentLogLength = currentLogs?.Length ?? 0;
        _lastSeenLogLength.TryGetValue(activeRun.Id, out int lastLogLength);

        if (currentLogLength > lastLogLength)
        {
            _lastSeenLogLength[activeRun.Id] = currentLogLength;
            activeRun.RecordActivity(now);
        }

        Result<LatestBranchCommit> commitResult = await postExitProviderQueries.GetLatestBranchCommitAsync(
            activeRun.MonitoredRepositoryId,
            activeRun.BranchName.Value,
            cancellationToken);

        if (commitResult is Result<LatestBranchCommit>.Success { Value: LatestBranchCommit latestCommit })
        {
            _lastSeenCommitSha.TryGetValue(activeRun.Id, out string? lastSha);

            if (latestCommit.Sha != lastSha)
            {
                _lastSeenCommitSha[activeRun.Id] = latestCommit.Sha;
                activeRun.RecordCommit(CommitMarker.Create(now, latestCommit.Sha, latestCommit.Message));
            }
        }

        if (activeRun.DomainEvents.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await domainEventDispatcher.DispatchAsync(activeRun.DomainEvents, cancellationToken);
            activeRun.ClearDomainEvents();
        }
    }

    /// <summary>
    /// When <paramref name="failureReason"/> is <see cref="FailureReason.AuthInvalid"/>,
    /// publishes <see cref="WorkerAuthenticationFailedEvent"/> so the Credentials module
    /// can transition the account state and broadcast the dispatch-paused notification.
    /// </summary>
    private async Task PublishAuthFailedIfNeededAsync(
        DbContext dbContext,
        IIntegrationEventDispatcher integrationEventDispatcher,
        ActiveRun activeRun,
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
        await TryDispatchAsync(
            integrationEventDispatcher,
            new WorkerAuthenticationFailedEvent(
                activeRun.Id.Value,
                activeRun.IssueId.Value,
                failureReason.Summary),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Auth-invalid exit detected; WorkerAuthenticationFailed published.");
    }

    /// <summary>
    /// When the usage-limit reset time has passed and auto-resume is enabled, calls
    /// <see cref="GlobalSettings.ResumeDispatch"/>, persists, and publishes
    /// <see cref="DispatchResumedEvent"/>. Returns <c>true</c> when a resume was performed.
    /// </summary>
    private async Task<bool> TryAutoResumeAsync(
        DbContext dbContext,
        IIntegrationEventDispatcher integrationEventDispatcher,
        DispatchPauseState pauseState,
        CancellationToken cancellationToken)
    {
        if (!pauseState.UsageLimitResetsAt.HasValue ||
            pauseState.UsageLimitResetsAt.Value > DateTimeOffset.UtcNow ||
            !pauseState.AutoResumeOnUsageReset)
        {
            return false;
        }

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            return false;
        }

        settings.ResumeDispatch();
        // Enqueue before save so the outbox interceptor harvests DispatchResumed atomically
        // with the settings change. DispatchResumed has a durable consumer (DispatchResumedHandler
        // in Issues re-queues failed issues), so it must go through the outbox.
        await integrationEventDispatcher.DispatchAsync([new DispatchResumedEvent()], cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Usage limit reset time has passed; dispatch auto-resumed (was paused until {ResetsAt}).",
            pauseState.UsageLimitResetsAt.Value);

        return true;
    }

    private async Task<bool> RemoveUnknownContainersAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)> containers =
                await orchestrator.ListByLabelAsync(cancellationToken);

            List<WorkerRunId> activeRunIdList = await dbContext.Set<ActiveRun>()
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            HashSet<WorkerRunId> activeRunIds = activeRunIdList.ToHashSet();

            foreach ((ContainerId containerId, WorkerRunId workerRunId) in containers)
            {
                if (activeRunIds.Contains(workerRunId))
                {
                    continue;
                }

                await orchestrator.StopAndRemoveAsync(containerId.Value, cancellationToken);
            }

            return true;
        }
#pragma warning disable CA1031 // Docker daemon failures during startup must not crash the BackgroundService; the warning log surfaces the issue without blocking reconciliation.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            if (DockerDaemonConnectivity.IsUnreachable(ex, cancellationToken))
            {
                logger.LogWarning(
                    "Docker daemon unreachable during startup reconciliation orphan scan; deferring until next tick.");
                return false;
            }

            logger.LogWarning(
                ex,
                "Docker scan failed during startup reconciliation; skipping orphaned container removal.");
            return true;
        }
    }

    private async Task TryDispatchAsync(
        IIntegrationEventDispatcher integrationEventDispatcher,
        IEnumerable<IIntegrationEvent> events,
        Guid workerRunId,
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
    /// System-level overload: no worker run is associated with the dispatch, so the failure
    /// message references only the event type rather than a meaningless empty GUID.
    /// </summary>
    private async Task TryDispatchAsync(
        IIntegrationEventDispatcher integrationEventDispatcher,
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
        IIntegrationEventProcessor processor,
        IIntegrationEvent systemEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await processor.ProcessAsync(Guid.NewGuid(), systemEvent, cancellationToken);
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
        IWorkerOrchestrator orchestrator,
        string containerId,
        Guid workerRunId,
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

    private async Task TryStopContainerAsync(
        IWorkerOrchestrator orchestrator,
        string containerId,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.StopContainerAsync(containerId, cancellationToken);
        }
#pragma warning disable CA1031 // Best-effort stop before log capture on timeout path; Docker exceptions must not crash the BackgroundService tick or prevent log capture and cleanup.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to stop container {ContainerId} for WorkerRun {WorkerRunId}.",
                containerId,
                workerRunId);
        }
    }

    private async Task<string?> TryGetLogsAsync(
        IWorkerOrchestrator orchestrator,
        string containerId,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            string? output = await orchestrator.GetLogsAsync(containerId, LogTailLines, cancellationToken);

            if (output is null)
            {
                return null;
            }

            return output.Length > MaxContainerOutputLength
                ? output[..MaxContainerOutputLength]
                : output;
        }
#pragma warning disable CA1031 // Best-effort log capture before transition; Docker exceptions must not crash the BackgroundService tick or prevent the failure transition.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to capture logs for container {ContainerId} (WorkerRun {WorkerRunId}).",
                containerId,
                workerRunId);
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

    private const int LogTailLines = 500;
    private const int MaxContainerOutputLength = 65_536;
}
