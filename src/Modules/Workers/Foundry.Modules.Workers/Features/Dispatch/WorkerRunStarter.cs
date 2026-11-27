using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

/// <summary>
/// Starts each <see cref="StartingRun"/> that <see cref="IssueClaimedHandler"/> committed with its claim, off the outbox
/// relay so a slow start never delays event delivery. It wakes on <see cref="WorkerRunStartSignal"/> or every
/// <see cref="Interval"/>, and starts the ready runs concurrently, each in its own scope.
/// </summary>
internal sealed class WorkerRunStarter : PeriodicBackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerRunStartSignal _signal;
    private readonly ILogger<WorkerRunStarter> _log;
    private readonly Lock _inFlightLock = new();
    private readonly HashSet<Task> _inFlight = [];

    // Explicit constructor required — PeriodicBackgroundService has a protected constructor,
    // so primary constructors are not available here.
    public WorkerRunStarter(
        IServiceScopeFactory scopeFactory,
        WorkerRunStartSignal signal,
        ILogger<WorkerRunStarter> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _log = logger;
    }

    protected override TimeSpan TickInterval => Interval;

    protected override string ServiceName => nameof(WorkerRunStarter);

    /// <summary>Runs one tick and waits for every start it launched, so tests observe the finished starts.</summary>
    internal async Task TickForTest(CancellationToken cancellationToken)
    {
        await TickAsync(cancellationToken);
        await WhenInFlightCompleteAsync();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitForWakeAsync(stoppingToken);

                while (_signal.Reader.TryRead(out bool _))
                {
                }

                await RunGuardedTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Clean shutdown.
        }
        finally
        {
            // The stopping token cancels the in-flight starts; an interrupted run stays starting for StaleStartingRunService.
            await WhenInFlightCompleteAsync();
        }
    }

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();

        List<StartingRun> ready = await db.Set<StartingRun>()
            .Where(run => run.Claim != null && run.StartAttemptedAt == null)
            .ToListAsync(cancellationToken);

        foreach (StartingRun run in ready)
        {
            // Committed before the start launches, so no later tick (or restart) picks the run again.
            RunSourceToken sourceToken = run.BeginStartAttempt(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            Launch(run.Id, sourceToken, cancellationToken);
        }
    }

    private void Launch(WorkerRunId workerRunId, RunSourceToken sourceToken, CancellationToken stoppingToken)
    {
        Task start = Task.Run(() => StartOneAsync(workerRunId, sourceToken, stoppingToken), CancellationToken.None);

        lock (_inFlightLock)
        {
            _inFlight.Add(start);
        }

        _ = start.ContinueWith(
            completed =>
            {
                lock (_inFlightLock)
                {
                    _inFlight.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task StartOneAsync(WorkerRunId workerRunId, RunSourceToken sourceToken, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            WorkerRunStart start = scope.ServiceProvider.GetRequiredService<WorkerRunStart>();
            await start.StartAsync(workerRunId, sourceToken, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stopping: the run stays starting for StaleStartingRunService.
        }
#pragma warning disable CA1031 // One run's failure must not stop the others; the run stays starting, and StaleStartingRunService fails it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(
                ex,
                "Starting worker run {WorkerRunId} threw; StaleStartingRunService fails it.",
                workerRunId);
        }
    }

    private async Task WaitForWakeAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource heartbeat = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        heartbeat.CancelAfter(Interval);

        try
        {
            await _signal.Reader.WaitToReadAsync(heartbeat.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The heartbeat elapsed: tick anyway, so a run committed without a signal is still started.
        }
    }

    private async Task RunGuardedTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A tick failure must not stop the starter; the next wake retries the runs not yet attempted.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogError(ex, "Unhandled exception in {ServiceName} tick.", ServiceName);
        }
    }

    private Task WhenInFlightCompleteAsync()
    {
        Task[] inFlight;
        lock (_inFlightLock)
        {
            inFlight = [.. _inFlight];
        }

        // StartOneAsync handles its own failures, so these tasks never fault.
        return Task.WhenAll(inFlight);
    }
}
