using System.Runtime.CompilerServices;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class Reconciliation : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenFirstTickAndContainerNotFound_TransitionsOrphanedRunToFailedRun()
    {
        // Arrange — resolver maps null-exit with no MR and no commits → NonZeroExit(-1)
        SeedActiveRun("orphaned-container");
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.NotFound());
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(-1);
    }

    [Fact]
    public async Task WhenFirstTickAndContainerStillRunning_RunRemainsActive()
    {
        // Arrange
        SeedActiveRun("running-container");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.Available(runningStatus));
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithZero_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("exited-zero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.Available(exitedStatus));
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithNonZero_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("exited-nonzero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.Available(exitedStatus));
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExited_GetStatusCalledOnce()
    {
        // Arrange
        SeedActiveRun("exited-on-reconcile-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.Available(exitedStatus));
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — status fetched once during reconciliation; MonitorRunAsync must reuse it
        orchestrator.GetStatusCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenSecondTick_ReconciliationDoesNotRunAgain()
    {
        // Arrange — seed run, first tick will reconcile (container missing → FailedRun)
        // Then seed another active run; second tick should NOT reconcile it (it stays Active)
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.NotFound());
        WorkerDispatchService sut = BuildService(orchestrator);

        // First tick: reconciles the orphaned run (no active runs seeded yet, so nothing happens)
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed a new ActiveRun AFTER the first tick
        SeedActiveRun("post-reconcile-container");

        // Act — second tick should skip reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — the new ActiveRun was NOT touched by reconciliation (stays Active because
        // reconciliation only runs once; normal monitoring handles it but orchestrator returns null
        // which would transition it — but that's via normal monitoring, not reconciliation)
        // What we verify: reconciliation ran exactly once (GetStatusAsync call count matches)
        orchestrator.GetStatusCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenReconcileTickSeesUnreachableProbe_ReconciledIsNotLatched()
    {
        // Arrange — active run exists, first tick: reconcile probe returns Unreachable → _reconciled stays false
        SeedActiveRun("unreachable-reconcile-container");
        ReconciliationStubWorkerOrchestrator orchestrator = ReconciliationStubWorkerOrchestrator.WithUnreachableDaemon();
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — first tick: reconcile sees Unreachable → _reconciled must NOT be latched
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — second tick runs reconciliation again.
        // Each tick calls GetStatusAsync twice: once in reconcile and once in monitor.
        // Tick 1: reconcile(1) + monitor(1) = 2. Tick 2: reconcile(1) + monitor(1) = 4.
        // If _reconciled were latched after tick 1 (wrong), tick 2 would only call monitor: total = 3.
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        orchestrator.GetStatusCallCount.ShouldBe(4);
    }

    [Fact]
    public async Task WhenReconcileAndAllProbesReachable_ReturnsTrue()
    {
        // Arrange — two active runs, all probes reachable (NotFound)
        SeedActiveRun("container-a");
        SeedActiveRun("container-b");
        ReconciliationStubWorkerOrchestrator orchestrator = new(probe: new WorkerStatusProbe.NotFound());
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        bool result = await sut.ReconcileOrphanedRunsForTestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenReconcileAndAnyProbeUnreachable_ReturnsFalse()
    {
        // Arrange — one active run, probe returns Unreachable
        SeedActiveRun("container-unreachable");
        ReconciliationStubWorkerOrchestrator orchestrator = ReconciliationStubWorkerOrchestrator.WithUnreachableDaemon();
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        bool result = await sut.ReconcileOrphanedRunsForTestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeFalse();
    }

    internal sealed class ReconciliationStubWorkerOrchestrator : IWorkerOrchestrator
    {
        private readonly WorkerStatusProbe _probe;

        public ReconciliationStubWorkerOrchestrator(WorkerStatusProbe probe)
        {
            _probe = probe;
        }

        public static ReconciliationStubWorkerOrchestrator WithUnreachableDaemon()
            => new(new WorkerStatusProbe.Unreachable());

        public int GetStatusCallCount { get; private set; }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in reconciliation tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        {
            GetStatusCallCount++;
            return Task.FromResult(_probe);
        }

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
