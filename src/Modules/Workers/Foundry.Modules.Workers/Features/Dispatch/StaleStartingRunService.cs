using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Periodic background service that detects <see cref="StartingRun"/> rows stranded in the
/// <c>starting</c> state (i.e. the container never became active) and fails them so the
/// associated issue can retry.
/// </summary>
internal sealed class StaleStartingRunService : PeriodicBackgroundService
{
    internal static readonly TimeSpan StaleStartingRunThreshold = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleStartingRunService> _log;

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public StaleStartingRunService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleStartingRunService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _log = logger;
    }

    protected override TimeSpan TickInterval => Interval;

    protected override string ServiceName => nameof(StaleStartingRunService);

    /// <summary>
    /// Exposes <see cref="TickAsync"/> for direct invocation in unit tests without
    /// spinning up the full <see cref="PeriodicBackgroundService.ExecuteAsync"/> loop.
    /// </summary>
    internal Task TickForTest(CancellationToken cancellationToken) => TickAsync(cancellationToken);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        IWorkerOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IWorkerOrchestrator>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)> labelledContainers;

        try
        {
            labelledContainers = await orchestrator.ListByLabelAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Docker connectivity failures during the stale-run sweep must not crash the BackgroundService; the warning log surfaces the issue without blocking future ticks.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            if (DockerDaemonConnectivity.IsUnreachable(ex, cancellationToken))
            {
                _log.LogWarning(
                    "Docker daemon unreachable during stale starting-run sweep; deferring until next tick.");
            }
            else
            {
                _log.LogWarning(
                    ex,
                    "Docker scan failed during stale starting-run sweep; deferring until next tick.");
            }

            return;
        }

        // Reap containers whose run id falls outside slot occupancy (starting ∪ active).
        IReadOnlySet<WorkerRunId> slotRunIds = await db.GetSlotOccupancyRunIdsAsync(cancellationToken);

        foreach ((ContainerId containerId, WorkerRunId workerRunId) in labelledContainers)
        {
            if (slotRunIds.Contains(workerRunId))
            {
                continue;
            }

            await TryStopAndRemoveAsync(orchestrator, containerId.Value, workerRunId.Value, cancellationToken);
        }

        // Load StartingRuns for staleness filtering (applied in memory — SQLite cannot translate DateTimeOffset).
        List<StartingRun> startingRuns = await db.Set<StartingRun>()
            .ToListAsync(cancellationToken);

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - StaleStartingRunThreshold;

        Dictionary<WorkerRunId, ContainerId> containerByRunId = labelledContainers
            .ToDictionary(c => c.WorkerRunId, c => c.ContainerId);

        foreach (StartingRun startingRun in startingRuns)
        {
            if (startingRun.CreatedAt > cutoff)
            {
                // Not yet stale — leave untouched.
                continue;
            }

            await TryFailStaleRunAsync(
                db,
                orchestrator,
                domainEventDispatcher,
                startingRun,
                containerByRunId,
                cancellationToken);
        }
    }

    private async Task TryFailStaleRunAsync(
        DbContext db,
        IWorkerOrchestrator orchestrator,
        IDomainEventDispatcher domainEventDispatcher,
        StartingRun startingRun,
        Dictionary<WorkerRunId, ContainerId> containerByRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Reload inside the try block to get a tracked instance for the state transition.
            StartingRun? live = await db.Set<StartingRun>()
                .FirstOrDefaultAsync(r => r.Id == startingRun.Id, cancellationToken);

            if (live is null)
            {
                // Concurrently transitioned (e.g. container reported back) — skip.
                return;
            }

            // When the stale run has a labelled container, stop+remove it before failing the run.
            if (containerByRunId.TryGetValue(live.Id, out ContainerId containerId))
            {
                await TryStopAndRemoveAsync(orchestrator, containerId.Value, live.Id.Value, cancellationToken);
            }

            FailedRun failed = live.Fail(new FailureReason.ContainerError("Container did not start within the allowed time."));
            await db.TransitionAsync(live, failed, domainEventDispatcher, cancellationToken);

            _log.LogWarning(
                "Stale starting run {WorkerRunId} failed after exceeding the {ThresholdMinutes}-minute threshold.",
                live.Id,
                StaleStartingRunThreshold.TotalMinutes);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent transition won the race — no action needed.
        }
#pragma warning disable CA1031 // Per-run failure must not abort the entire tick; the error log surfaces the issue without interrupting the loop.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _log.LogWarning(
                ex,
                "Stale starting-run sweep failed for run {WorkerRunId}.",
                startingRun.Id.Value);
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
#pragma warning disable CA1031 // Best-effort container removal; Docker exceptions must not crash the BackgroundService tick.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _log.LogWarning(
                ex,
                "Failed to stop and remove container {ContainerId} for stale starting run {WorkerRunId}.",
                containerId,
                workerRunId);
        }
    }
}
