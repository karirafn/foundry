using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using DispatchPausedEvent = Foundry.Modules.Workers.Contracts.DispatchPaused;
using DispatchPausedForAuthInvalidEvent = Foundry.Modules.Workers.Contracts.DispatchPausedForAuthInvalid;
using DispatchResumedEvent = Foundry.Modules.Workers.Contracts.DispatchResumed;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features;

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
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
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

        if (!_reconciled)
        {
            await ReconcileOrphanedRunsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                resolver,
                postExitProviderQueries,
                containerOutputParser,
                timeoutMinutes,
                defaultCooldownMinutes,
                activeRuns,
                cancellationToken);
            _reconciled = true;
        }

        await MonitorActiveRunsAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
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

        if (!autoResumed && (pauseState.IsDispatchPaused || pauseState.UsageLimitResetsAt.HasValue || pauseState.AuthInvalidPause))
        {
            logger.LogDebug(
                "Dispatch skipped: dispatch is paused (IsDispatchPaused={IsDispatchPaused}, UsageLimitResetsAt={UsageLimitResetsAt}, AuthInvalidPause={AuthInvalidPause}).",
                pauseState.IsDispatchPaused,
                pauseState.UsageLimitResetsAt,
                pauseState.AuthInvalidPause);
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
    }

    private async Task ReconcileOrphanedRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        WorkerOutcomeResolver resolver,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        await RemoveUnknownContainersAsync(dbContext, orchestrator, cancellationToken);

        List<ActiveRun> runsToRemove = [];

        foreach (ActiveRun activeRun in activeRuns)
        {
            WorkerStatus? status = await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

            if (status is null)
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
                    domainEventDispatcher,
                    cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation.",
                    activeRun.Id,
                    activeRun.ContainerId.Value);
            }
            else if (!status.IsRunning)
            {
                await MonitorRunAsync(
                    dbContext,
                    orchestrator,
                    integrationEventDispatcher,
                    domainEventDispatcher,
                    resolver,
                    postExitProviderQueries,
                    containerOutputParser,
                    timeoutMinutes,
                    defaultCooldownMinutes,
                    activeRun,
                    cancellationToken,
                    knownStatus: status);
                runsToRemove.Add(activeRun);
            }
        }

        foreach (ActiveRun run in runsToRemove)
        {
            activeRuns.Remove(run);
        }
    }

    private async Task MonitorActiveRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
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
        IDomainEventDispatcher domainEventDispatcher,
        WorkerOutcomeResolver resolver,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        CancellationToken cancellationToken,
        WorkerStatus? knownStatus = null)
    {
        WorkerStatus? status = knownStatus
            ?? await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

        if (status is null)
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
                domainEventDispatcher,
                cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} container {ContainerId} not found; marked as {OutcomeType}.",
                activeRun.Id,
                activeRun.ContainerId.Value,
                outcome.GetType().Name);
            return;
        }

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
            domainEventDispatcher,
            cancellationToken);
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
                await dbContext.TransitionAsync(activeRun, completedRun, domainEventDispatcher, cancellationToken);
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
                await dbContext.TransitionAsync(activeRun, reviewRun, domainEventDispatcher, cancellationToken);
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
                await dbContext.TransitionAsync(activeRun, unchangedRun, domainEventDispatcher, cancellationToken);
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
                    continuable.ContainerOutput,
                    continuable.Summary);
                await dbContext.TransitionAsync(
                    activeRun,
                    continuableFailed,
                    domainEventDispatcher,
                    cancellationToken);
                await PersistUsageLimitIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    continuable.FailureReason,
                    cancellationToken);
                await PersistAuthInvalidIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    continuable.FailureReason,
                    cancellationToken);
                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        continuable.FailureReason.Summary,
                        Category: continuable.FailureReason.CategoryToken,
                        BranchName: continuable.BranchName.Value)],
                    activeRun.Id.Value,
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
                    failure.ContainerOutput,
                    failure.Summary);
                await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);
                await PersistUsageLimitIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    failure.FailureReason,
                    cancellationToken);
                await PersistAuthInvalidIfNeededAsync(
                    dbContext,
                    integrationEventDispatcher,
                    failure.FailureReason,
                    cancellationToken);
                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        failure.FailureReason.Summary,
                        Category: failure.FailureReason.CategoryToken,
                        BranchName: null)],
                    activeRun.Id.Value,
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
        IIntegrationEventDispatcher integrationEventDispatcher,
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
            await TryDispatchAsync(
                integrationEventDispatcher,
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
    /// sets <see cref="GlobalSettings.AuthInvalidPause"/>, persists it, and dispatches
    /// <see cref="DispatchPausedForAuthInvalidEvent"/>.
    /// </summary>
    private async Task PersistAuthInvalidIfNeededAsync(
        DbContext dbContext,
        IIntegrationEventDispatcher integrationEventDispatcher,
        FailureReason failureReason,
        CancellationToken cancellationToken)
    {
        if (failureReason is not FailureReason.AuthInvalid)
        {
            return;
        }

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Auth-invalid exit detected but could not persist pause — no GlobalSettings row exists.");
            return;
        }

        bool wasAlreadyPaused = settings.AuthInvalidPause;
        settings.PauseForAuthInvalid();
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!wasAlreadyPaused)
        {
            await TryDispatchAsync(
                integrationEventDispatcher,
                new DispatchPausedForAuthInvalidEvent(),
                cancellationToken);
        }

        logger.LogWarning("Auth-invalid exit detected; dispatch paused until credentials are refreshed.");
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
        await dbContext.SaveChangesAsync(cancellationToken);

        await integrationEventDispatcher.DispatchAsync([new DispatchResumedEvent()], cancellationToken);

        logger.LogInformation(
            "Usage limit reset time has passed; dispatch auto-resumed (was paused until {ResetsAt}).",
            pauseState.UsageLimitResetsAt.Value);

        return true;
    }

    private async Task RemoveUnknownContainersAsync(
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
        }
#pragma warning disable CA1031 // Docker daemon failures during startup must not crash the BackgroundService; the warning log surfaces the issue without blocking reconciliation.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Docker scan failed during startup reconciliation; skipping orphaned container removal.");
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

    private const int LogTailLines = 500;
    private const int MaxContainerOutputLength = 65_536;
}
