using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed class WorkerDispatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

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

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);

        if (!_reconciled)
        {
            await ReconcileOrphanedRunsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                timeoutMinutes,
                activeRuns,
                cancellationToken);
            _reconciled = true;
        }

        await MonitorActiveRunsAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
            domainEventDispatcher,
            timeoutMinutes,
            activeRuns,
            cancellationToken);

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
        int timeoutMinutes,
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
                    timeoutMinutes,
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
        int timeoutMinutes,
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
                timeoutMinutes,
                activeRun,
                cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        int timeoutMinutes,
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

        if (status.ExitCode == 0)
        {
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
                "Worker run {WorkerRunId} completed successfully (branch: {BranchName}).",
                activeRun.Id,
                activeRun.BranchName.Value);

            await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
        }
        else
        {
            int exitCode = status.ExitCode ?? -1;
            string exitReason = $"Non-zero exit code: {exitCode}";

            string? containerOutput = await TryGetLogsAsync(
                orchestrator,
                activeRun.ContainerId.Value,
                activeRun.Id.Value,
                cancellationToken);

            FailedRun failedRun = activeRun.Fail(new FailureReason.NonZeroExit(exitCode), containerOutput);
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    exitReason,
                    BranchName: activeRun.BranchName.Value)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} exited with code {ExitCode}.",
                activeRun.Id,
                exitCode);

            await TryStopAndRemoveAsync(orchestrator, activeRun.ContainerId.Value, activeRun.Id.Value, cancellationToken);
        }
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
