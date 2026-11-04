using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using DispatchResumedEvent = Foundry.Modules.Workers.Contracts.DispatchResumed;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Periodic dispatch loop. Each tick reconciles orphaned runs once (after startup), delegates
/// per-run monitoring to <see cref="WorkerRunMonitor"/>, auto-resumes an expired usage-limit
/// pause, and — when every dispatch gate passes — reserves a slot and publishes
/// <see cref="WorkerCapacityAvailable"/>.
/// </summary>
internal sealed class WorkerDispatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkerDispatchService> logger,
    TimeProvider timeProvider) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    // Safe without locking — PeriodicTimer loop is single-threaded
    private bool _reconciled;

    protected override TimeSpan TickInterval => Interval;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        // Bridge handlers (WorkerRunFailedBridgeHandler) enqueue integration events inside the
        // TransitionAsync transaction via the outbox interceptor. Using the raw dispatcher means
        // a bridge-handler throw rolls back the entire transaction (state + outbox row) atomically.
        // In production, OutboxIntegrationEventDispatcher only enqueues into a list and never throws,
        // so the bridge handler path is safe without any fault-tolerant wrapper.
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        IGlobalSettingsQueries settingsQueries =
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        ICredentialGate credentialGate =
            scope.ServiceProvider.GetRequiredService<ICredentialGate>();
        WorkerRunMonitor runMonitor = scope.ServiceProvider.GetRequiredService<WorkerRunMonitor>();

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);

        if (!_reconciled)
        {
            _reconciled = await runMonitor.ReconcileOrphanedRunsAsync(activeRuns, timeoutMinutes, cancellationToken);
        }

        await runMonitor.MonitorActiveRunsAsync(activeRuns, timeoutMinutes, cancellationToken);

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

        int slotOccupancy = await dbContext.GetSlotOccupancyCountAsync(cancellationToken);
        int maxConcurrent = await settingsQueries.GetMaxConcurrentAsync(cancellationToken);

        if (slotOccupancy >= maxConcurrent)
        {
            logger.LogDebug(
                "Dispatch skipped: {SlotOccupancy}/{MaxConcurrent} slots in use.",
                slotOccupancy,
                maxConcurrent);
            return;
        }

        WorkerRunId workerRunId = WorkerRunId.New();

        // Persist the reservation before dispatching so the slot is durably held as soon as
        // the authorization is published. The reservation and the outbox row are written in
        // one SaveChangesAsync call, making the promise atomic (D5, ADR 0069).
        dbContext.Set<DispatchReservation>().Add(
            DispatchReservation.Reserve(workerRunId, timeProvider.GetUtcNow()));

        await TryDispatchAsync(
            integrationEventDispatcher,
            [new WorkerCapacityAvailable(workerRunId)],
            workerRunId,
            cancellationToken);

        // Harvest: enqueue-before-save ensures WorkerCapacityAvailable is written atomically
        // to outbox_messages alongside the reservation row. Without this save the scoped
        // collector holds the event but it is never drained and the IssueClaimed downstream
        // transition is silently lost.
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Exposed as <c>internal</c> for unit tests that need to verify the reachability signal
    /// returned by <see cref="WorkerRunMonitor.ReconcileOrphanedRunsAsync"/>.
    /// </summary>
    internal async Task<bool> ReconcileOrphanedRunsForTestAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IGlobalSettingsQueries settingsQueries =
            scope.ServiceProvider.GetRequiredService<IGlobalSettingsQueries>();
        WorkerRunMonitor runMonitor = scope.ServiceProvider.GetRequiredService<WorkerRunMonitor>();

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);

        return await runMonitor.ReconcileOrphanedRunsAsync(activeRuns, timeoutMinutes, cancellationToken);
    }

    /// <summary>
    /// When the usage-limit reset time has passed and auto-resume is enabled, calls
    /// <see cref="GlobalSettings.ClearUsageLimitPause"/>, persists, and publishes
    /// <see cref="DispatchResumedEvent"/> only when no manual pause remains.
    /// Returns <c>true</c> when dispatch is now fully unpaused (safe for the caller to
    /// bypass the stale pause-state gate), <c>false</c> when a manual pause still holds.
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

        bool fullyUnpaused = settings.ClearUsageLimitPause();

        if (fullyUnpaused)
        {
            // Enqueue before save so the outbox interceptor harvests DispatchResumed atomically
            // with the settings change. DispatchResumed has a durable consumer (DispatchResumedHandler
            // in Issues re-queues failed issues), so it must go through the outbox.
            await integrationEventDispatcher.DispatchAsync([new DispatchResumedEvent()], cancellationToken);
        }
        // When a manual pause remains, suppress DispatchResumed — the operator has explicitly
        // halted dispatch and the event would falsely signal that dispatch has resumed.

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Usage limit reset time has passed; usage-limit pause cleared (was paused until {ResetsAt}). "
            + "Dispatch fully unpaused: {FullyUnpaused}.",
            pauseState.UsageLimitResetsAt.Value,
            fullyUnpaused);

        return fullyUnpaused;
    }

    private async Task TryDispatchAsync(
        IIntegrationEventDispatcher integrationEventDispatcher,
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
}
