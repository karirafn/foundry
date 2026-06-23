using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

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
    ILogger<WorkerDispatchService> logger,
    TimeSpan? prRetryDelay = null) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPrRetryDelay = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _prRetryDelay = prRetryDelay ?? DefaultPrRetryDelay;

    // Safe without locking — PeriodicTimer loop is single-threaded
    private bool _reconciled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteTickAsync(stoppingToken);
        }
    }

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
            postExitProviderQueries,
            containerOutputParser,
            timeoutMinutes,
            defaultCooldownMinutes,
            activeRuns,
            cancellationToken);

        DispatchPauseState pauseState = await settingsQueries.GetDispatchPauseStateAsync(cancellationToken);

        bool autoResumed = await TryAutoResumeAsync(dbContext, integrationEventDispatcher, pauseState, cancellationToken);

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
        await integrationEventDispatcher.DispatchAsync(
            [new WorkerCapacityAvailable(workerRunId)],
            cancellationToken);
    }

    private async Task ReconcileOrphanedRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
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
                FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Orphaned after restart"));
                await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);
                runsToRemove.Add(activeRun);

                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        "Orphaned after restart",
                        BranchName: activeRun.BranchName.Value)],
                    activeRun.Id.Value,
                    cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation; marking failed.",
                    activeRun.Id,
                    activeRun.ContainerId.Value);

                await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
            }
            else if (!status.IsRunning)
            {
                await MonitorRunAsync(
                    dbContext,
                    orchestrator,
                    integrationEventDispatcher,
                    domainEventDispatcher,
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
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int timeoutMinutes,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        CancellationToken cancellationToken,
        WorkerStatus? knownStatus = null)
    {
        WorkerStatus? status = knownStatus ?? await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

        if (status is null)
        {
            FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Container not found"));
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    "Container not found",
                    BranchName: activeRun.BranchName.Value)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} container {ContainerId} not found; marking failed.",
                activeRun.Id,
                activeRun.ContainerId.Value);

            await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
            return;
        }

        if (status.IsRunning)
        {
            DateTimeOffset timeout = activeRun.StartedAt.AddMinutes(timeoutMinutes);
            if (DateTimeOffset.UtcNow >= timeout)
            {
                await TryStopContainerAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);

                string? containerOutput = await TryGetLogsAsync(
                    orchestrator,
                    activeRun.ContainerId.Value,
                    activeRun.Id.Value,
                    cancellationToken);

                FailedRun timedOut = activeRun.Fail(new FailureReason.TimedOut(), containerOutput);
                await dbContext.TransitionAsync(activeRun, timedOut, domainEventDispatcher, cancellationToken);

                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        "Timed out",
                        BranchName: activeRun.BranchName.Value)],
                    activeRun.Id.Value,
                    cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} timed out after {TimeoutMinutes} minutes; container stopped.",
                    activeRun.Id,
                    timeoutMinutes);

                await TryRemoveContainerAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
            }

            return;
        }

        await ProcessExitedRunAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
            domainEventDispatcher,
            postExitProviderQueries,
            containerOutputParser,
            defaultCooldownMinutes,
            activeRun,
            status,
            cancellationToken);
    }

    private async Task ProcessExitedRunAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        WorkerStatus status,
        CancellationToken cancellationToken)
    {
        Result<bool> commitsResult = await postExitProviderQueries.HasBranchCommitsAsync(
            activeRun.MonitoredRepositoryId,
            activeRun.BranchName.Value,
            cancellationToken);

        if (commitsResult is Result<bool>.Failure commitsFailure)
        {
            logger.LogWarning(
                "Failed to check branch commits for run {RunId}: {Error}",
                activeRun.Id,
                commitsFailure.Error);
            return;
        }

        bool hasCommits = commitsResult is Result<bool>.Success { Value: true };

        if (status.ExitCode == 0 && hasCommits)
        {
            await ProcessSuccessWithCommitsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                postExitProviderQueries,
                containerOutputParser,
                defaultCooldownMinutes,
                activeRun,
                cancellationToken);
        }
        else if (status.ExitCode == 0 && !hasCommits)
        {
            await ProcessSuccessWithoutCommitsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                containerOutputParser,
                defaultCooldownMinutes,
                activeRun,
                cancellationToken);
        }
        else
        {
            int exitCode = status.ExitCode ?? -1;
            string? containerOutput = await TryGetLogsAsync(
                orchestrator,
                activeRun.ContainerId.Value,
                activeRun.Id.Value,
                cancellationToken);

            await ProcessNonZeroExitAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                containerOutputParser,
                defaultCooldownMinutes,
                activeRun,
                exitCode,
                hasCommits,
                containerOutput,
                cancellationToken);
        }
    }

    private async Task ProcessSuccessWithCommitsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        IPostExitProviderQueries postExitProviderQueries,
        IContainerOutputParser containerOutputParser,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        string? prUrl = await TryGetPullRequestUrlAsync(
            postExitProviderQueries,
            activeRun,
            cancellationToken);

        if (prUrl is not null)
        {
            CompletedRun completed = activeRun.Complete(
                0,
                activeRun.BranchName,
                PullRequestUrl.From(prUrl));
            await dbContext.TransitionAsync(activeRun, completed, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunCompletedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    activeRun.BranchName.Value,
                    prUrl)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} completed with PR {PullRequestUrl} (branch: {BranchName}).",
                activeRun.Id,
                prUrl,
                activeRun.BranchName.Value);
        }
        else
        {
            // Commits pushed but no PR found after retries — check for usage limit in container output
            string? containerOutput = await TryGetLogsAsync(
                orchestrator,
                activeRun.ContainerId.Value,
                activeRun.Id.Value,
                cancellationToken);

            ContainerOutputParseResult parseResult = containerOutputParser.Parse(
                containerOutput,
                defaultCooldownMinutes);

            FailureReason failureReason = await ResolveFailureReasonAsync(
                dbContext,
                parseResult,
                new FailureReason.ContainerError("No pull request found after retries"),
                cancellationToken);

            FailedRun failedRun = activeRun.Fail(failureReason, containerOutput);
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    "No pull request found after retries",
                    BranchName: activeRun.BranchName.Value)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} exited with 0 but no PR found after retries (branch: {BranchName}).",
                activeRun.Id,
                activeRun.BranchName.Value);
        }

        await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
    }

    private async Task ProcessSuccessWithoutCommitsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        IContainerOutputParser containerOutputParser,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        string? containerOutput = await TryGetLogsAsync(
            orchestrator,
            activeRun.ContainerId.Value,
            activeRun.Id.Value,
            cancellationToken);

        ContainerOutputParseResult parseResult = containerOutputParser.Parse(
            containerOutput,
            defaultCooldownMinutes);

        if (parseResult is ContainerOutputParseResult.UsageLimited)
        {
            FailureReason failureReason = await ResolveFailureReasonAsync(
                dbContext,
                parseResult,
                new FailureReason.ContainerError("No commits and usage limited"),
                cancellationToken);

            FailedRun failedRun = activeRun.Fail(failureReason);
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    WorkerRunFailedEvent.UsageLimitedReason)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} completed with no commits (usage limited, branch: {BranchName}).",
                activeRun.Id,
                activeRun.BranchName.Value);
        }
        else
        {
            // Exit code 0 with no commits — unchanged
            CompletedRun completed = activeRun.Complete(0, activeRun.BranchName, null);
            await dbContext.TransitionAsync(activeRun, completed, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunCompletedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    activeRun.BranchName.Value,
                    null)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} completed with no commits (branch: {BranchName}).",
                activeRun.Id,
                activeRun.BranchName.Value);
        }

        await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
    }

    private async Task ProcessNonZeroExitAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        IContainerOutputParser containerOutputParser,
        int defaultCooldownMinutes,
        ActiveRun activeRun,
        int exitCode,
        bool hasCommits,
        string? containerOutput,
        CancellationToken cancellationToken)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(
            containerOutput,
            defaultCooldownMinutes);

        FailureReason failureReason = await ResolveFailureReasonAsync(
            dbContext,
            parseResult,
            new FailureReason.NonZeroExit(exitCode),
            cancellationToken);

        // Null branch name when no commits → FailedIssue; non-null → ContinuableFailedIssue
        string? branchNameForEvent = hasCommits ? activeRun.BranchName.Value : null;

        string exitReason = failureReason is FailureReason.UsageLimited
            ? WorkerRunFailedEvent.UsageLimitedReason
            : $"Non-zero exit code: {exitCode}";

        FailedRun failedRun = activeRun.Fail(failureReason, containerOutput);
        await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

        await TryDispatchAsync(
            integrationEventDispatcher,
            [new WorkerRunFailedEvent(
                activeRun.Id.Value,
                activeRun.IssueId.Value,
                exitReason,
                BranchName: branchNameForEvent)],
            activeRun.Id.Value,
            cancellationToken);

        if (failureReason is FailureReason.UsageLimited usageLimitedReason)
        {
            logger.LogWarning(
                "Worker run {WorkerRunId} exited with code {ExitCode} due to usage limit; resets at {ResetsAt}.",
                activeRun.Id,
                exitCode,
                usageLimitedReason.ResetsAt);
        }
        else
        {
            logger.LogWarning(
                "Worker run {WorkerRunId} exited with code {ExitCode} (commits: {HasCommits}).",
                activeRun.Id,
                exitCode,
                hasCommits);
        }

        await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
    }

    /// <summary>
    /// Resolves the failure reason from a container output parse result.
    /// If the parse result indicates a usage limit, sets <see cref="GlobalSettings.UsageLimitResetsAt"/>
    /// (extend-only; past times are ignored by <see cref="GlobalSettings.SetUsageLimitResetsAt"/>)
    /// and returns <see cref="FailureReason.UsageLimited"/>.
    /// Otherwise returns the <paramref name="fallbackReason"/>.
    /// </summary>
    private async Task<FailureReason> ResolveFailureReasonAsync(
        DbContext dbContext,
        ContainerOutputParseResult parseResult,
        FailureReason fallbackReason,
        CancellationToken cancellationToken)
    {
        if (parseResult is not ContainerOutputParseResult.UsageLimited usageLimited)
        {
            return fallbackReason;
        }

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning(
                "Usage limit detected but could not persist reset time — no GlobalSettings row exists.");
        }
        else
        {
            settings.SetUsageLimitResetsAt(usageLimited.ResetsAt);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Usage limit detected; dispatch paused until {ResetsAt}.",
                usageLimited.ResetsAt);
        }

        return new FailureReason.UsageLimited(usageLimited.ResetsAt);
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

    private async Task<string?> TryGetPullRequestUrlAsync(
        IPostExitProviderQueries postExitProviderQueries,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < PrRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_prRetryDelay, cancellationToken);
            }

            Result<string> prResult = await postExitProviderQueries.GetPullRequestByBranchAsync(
                activeRun.MonitoredRepositoryId,
                activeRun.BranchName.Value,
                cancellationToken);

            if (prResult is Result<string>.Success { Value: { Length: > 0 } prUrl })
            {
                return prUrl;
            }
        }

        return null;
    }

    private const int PrRetryAttempts = 3;

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
        catch (Exception ex)
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
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to dispatch integration event for WorkerRun {WorkerRunId}", workerRunId);
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
        catch (Exception ex)
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
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to stop container {ContainerId} for WorkerRun {WorkerRunId}.",
                containerId,
                workerRunId);
        }
    }

    private async Task TryRemoveContainerAsync(
        IWorkerOrchestrator orchestrator,
        string containerId,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await orchestrator.RemoveContainerAsync(containerId, cancellationToken);
        }
#pragma warning disable CA1031 // Best-effort container removal after terminal state transition; Docker exceptions must not crash the BackgroundService tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Failed to remove container {ContainerId} for WorkerRun {WorkerRunId} after timeout.",
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

            return output is not null && output.Length > MaxContainerOutputLength
                ? output[..MaxContainerOutputLength]
                : output;
        }
#pragma warning disable CA1031 // Best-effort log capture before transition; Docker exceptions must not crash the BackgroundService tick or prevent the failure transition.
        catch (Exception ex)
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
