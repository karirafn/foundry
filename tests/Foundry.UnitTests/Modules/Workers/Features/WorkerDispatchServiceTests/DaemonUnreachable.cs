using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class DaemonUnreachable : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenMonitoringTickAndDaemonUnreachable_RunRemainsActive()
    {
        // Arrange — active run exists, daemon returns Unreachable
        SeedActiveRun("unreachable-container");
        DaemonUnreachableStubOrchestrator orchestrator = new(new WorkerStatusProbe.Unreachable());
        WorkerDispatchService sut = BuildService(orchestrator);

        // Trigger reconciliation on the first tick so the second tick exercises the monitoring path.
        // First tick: no active runs seeded yet for reconcile (we seed after), so reconcile marks done.
        // Seed after first tick so the second tick hits MonitorActiveRunsAsync, not ReconcileOrphanedRunsAsync.
        // Actually: we seed before the first tick but the reconcile will see it — to isolate
        // MonitorActiveRunsAsync we run two ticks: first reconcile processes it (but Unreachable should
        // still leave it Active), then second tick monitors it (also Unreachable → still Active).
        //
        // Simplest path: run one tick — reconcile fires on first tick with Unreachable, run stays Active.
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run must still be ActiveRun: no terminal transition occurred
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenMonitoringTickAndDaemonUnreachable_NoContainerStopAttempted()
    {
        // Arrange
        SeedActiveRun("unreachable-container-2");
        DaemonUnreachableStubOrchestrator orchestrator = new(new WorkerStatusProbe.Unreachable());
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — first tick: reconcile sees Unreachable probe; second tick: monitor sees same
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — no stop-and-remove was attempted
        orchestrator.StopAndRemoveCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDaemonRecoversAfterUnreachable_RunTransitionsNormally()
    {
        // Arrange — tick 1: both reconcile and monitor return Unreachable (run stays Active).
        //            tick 2: reconcile runs again (_reconciled not latched because tick 1 saw Unreachable);
        //                    reconcile path (via MonitorRunAsync with knownProbe) transitions run → CompletedRun.
        SeedActiveRun("recovering-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);

        // unreachableCount=2: reconcile (tick 1) + monitor (tick 1) both see Unreachable.
        // From call 3 onwards, the stub returns Available(exited).
        DaemonUnreachableStubOrchestrator orchestrator = new(
            unreachableCallCount: 2,
            recoveredProbe: new WorkerStatusProbe.Available(exitedStatus));
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — first tick: reconcile + monitor both see Unreachable → run stays Active
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Verify still active after tick 1
        await using FoundryDbContext midDb = CreateDbContext();
        WorkerRun? midRun = await midDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        midRun.ShouldBeOfType<ActiveRun>();

        // Act — second tick: reconcile runs again (daemon was unreachable on tick 1, _reconciled still false);
        //        reconcile sees Available(exited) via MonitorRunAsync(knownProbe) → transitions run
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run transitioned to CompletedRun after daemon recovered
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    private sealed class DaemonUnreachableStubOrchestrator : IWorkerOrchestrator
    {
        private readonly int _unreachableCallCount;
        private readonly WorkerStatusProbe _recoveredProbe;
        private int _callCount;

        public int StopAndRemoveCallCount { get; private set; }

        public DaemonUnreachableStubOrchestrator(WorkerStatusProbe probe)
            : this(unreachableCallCount: int.MaxValue, recoveredProbe: probe)
        {
        }

        public DaemonUnreachableStubOrchestrator(int unreachableCallCount, WorkerStatusProbe recoveredProbe)
        {
            _unreachableCallCount = unreachableCallCount;
            _recoveredProbe = recoveredProbe;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        {
            _callCount++;
            WorkerStatusProbe probe = _callCount <= _unreachableCallCount
                ? new WorkerStatusProbe.Unreachable()
                : _recoveredProbe;
            return Task.FromResult(probe);
        }

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            StopAndRemoveCallCount++;
            return Task.CompletedTask;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in daemon tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
