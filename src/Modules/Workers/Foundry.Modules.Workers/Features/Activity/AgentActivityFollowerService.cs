using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Activity;

/// <summary>
/// Follows the container log of every <see cref="ActiveRun"/> and folds it into an in-memory
/// <see cref="AgentActivityProjection"/>, broadcasting changed snapshots over <c>WorkerHub</c>.
/// </summary>
/// <remarks>
/// Each follower reads its container log from the start, so after a Foundry restart the
/// projection is rebuilt by re-attaching. Followers are reconciled against the set of active
/// runs: a run leaving <see cref="ActiveRun"/> has its follower cancelled and its projection
/// disposed. A follower whose log stream faults is restarted with a fresh projection on the
/// next reconcile; one whose stream ends normally (the container exited) is left to be
/// disposed by the run transition. Changes are coalesced and flushed once per tick.
/// </remarks>
internal sealed class AgentActivityFollowerService(
    IServiceScopeFactory scopeFactory,
    IWorkerOrchestrator orchestrator,
    IAgentActivityBroadcaster broadcaster,
    AgentActivityStore store,
    TimeProvider timeProvider,
    ILogger<AgentActivityFollowerService> logger) : PeriodicBackgroundService(logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(10);

    // Safe without locking — PeriodicTimer loop is single-threaded.
    private readonly Dictionary<WorkerRunId, RunFollower> _followers = [];
    private DateTimeOffset? _lastReconciledAt;

    protected override TimeSpan TickInterval => Interval;

    protected override bool RunTickOnStartup => true;

    protected override Task TickAsync(CancellationToken cancellationToken)
        => ExecuteTickAsync(cancellationToken);

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (_lastReconciledAt is null || now - _lastReconciledAt >= ReconcileInterval)
        {
            await ReconcileAsync(cancellationToken);
            _lastReconciledAt = now;
        }

        await FlushAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        foreach (RunFollower follower in _followers.Values)
        {
            await follower.DisposeAsync();
        }

        _followers.Clear();
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        List<ActiveRun> activeRuns;

        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
            activeRuns = await db.Set<ActiveRun>().AsNoTracking().ToListAsync(cancellationToken);
        }

        HashSet<WorkerRunId> activeIds = [.. activeRuns.Select(r => r.Id)];

        foreach (RunFollower ended in _followers.Values.Where(f => !activeIds.Contains(f.WorkerRunId)).ToList())
        {
            _followers.Remove(ended.WorkerRunId);
            await ended.DisposeAsync();
            store.Remove(ended.WorkerRunId);
            await TryBroadcastAsync(
                () => broadcaster.BroadcastEndedAsync(
                    new AgentActivityEnded(ended.WorkerRunId, ended.IssueId),
                    cancellationToken),
                ended.WorkerRunId);
        }

        foreach (ActiveRun run in activeRuns)
        {
            if (_followers.TryGetValue(run.Id, out RunFollower? existing))
            {
                if (!existing.IsFaulted)
                {
                    continue;
                }

                await existing.DisposeAsync();
            }

            _followers[run.Id] = RunFollower.Start(
                run.Id,
                run.IssueId.Value,
                run.ContainerId.Value,
                orchestrator,
                timeProvider,
                logger);
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        foreach (RunFollower follower in _followers.Values)
        {
            if (follower.TakeChangedSnapshot() is not { } snapshot)
            {
                continue;
            }

            store.Set(snapshot);
            await TryBroadcastAsync(
                () => broadcaster.BroadcastActivityAsync(snapshot, cancellationToken),
                follower.WorkerRunId);
        }
    }

    private async Task TryBroadcastAsync(Func<Task> broadcast, WorkerRunId workerRunId)
    {
        try
        {
            await broadcast();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Presentation-only: a failed broadcast is superseded by the next snapshot.
            logger.LogWarning(ex, "Failed to broadcast agent activity for run {WorkerRunId}", workerRunId);
        }
    }

    private sealed class RunFollower : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private readonly AgentActivityProjection _projection = new();
        private readonly CancellationTokenSource _cts = new();
        private Task _pump = Task.CompletedTask;
        private bool _changed;
        private volatile bool _faulted;

        private RunFollower(WorkerRunId workerRunId, Guid issueId)
        {
            WorkerRunId = workerRunId;
            IssueId = issueId;
        }

        public WorkerRunId WorkerRunId { get; }

        public Guid IssueId { get; }

        public bool IsFaulted => _faulted;

        public static RunFollower Start(
            WorkerRunId workerRunId,
            Guid issueId,
            string containerId,
            IWorkerOrchestrator orchestrator,
            TimeProvider timeProvider,
            ILogger logger)
        {
            RunFollower follower = new(workerRunId, issueId);
            follower._pump = Task.Run(() => follower.PumpAsync(containerId, orchestrator, timeProvider, logger));
            return follower;
        }

        public AgentActivity? TakeChangedSnapshot()
        {
            lock (_gate)
            {
                if (!_changed)
                {
                    return null;
                }

                _changed = false;
                return _projection.Snapshot(WorkerRunId, IssueId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();

            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }

            _cts.Dispose();
        }

        private async Task PumpAsync(
            string containerId,
            IWorkerOrchestrator orchestrator,
            TimeProvider timeProvider,
            ILogger logger)
        {
            CancellationToken cancellationToken = _cts.Token;

            try
            {
                await foreach (string line in orchestrator.StreamLogsAsync(containerId, cancellationToken))
                {
                    lock (_gate)
                    {
                        _changed |= _projection.Apply(line, timeProvider.GetUtcNow());
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The pump must never throw into dispose: once cancelled (run transition or host
                // shutdown) the follower is finished, even if the stream faulted while cancelling.
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _faulted = true;
                logger.LogWarning(
                    ex,
                    "Agent activity follower for run {WorkerRunId} stopped; it will re-attach on the next reconcile",
                    WorkerRunId);
            }
        }
    }
}
