using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Modules.Workers.Features;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class Reconciliation : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenFirstTickAndContainerNotFound_TransitionsOrphanedRunToFailedRun()
    {
        // Arrange
        SeedActiveRun("orphaned-container");
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldBe("Orphaned after restart");
    }

    [Fact]
    public async Task WhenFirstTickAndContainerStillRunning_RunRemainsActive()
    {
        // Arrange
        SeedActiveRun("running-container");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithZero_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("exited-zero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenFirstTickAndContainerExitedWithNonZero_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("exited-nonzero-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSecondTick_ReconciliationDoesNotRunAgain()
    {
        // Arrange — seed run, first tick will reconcile (container missing → FailedRun)
        // Then seed another active run; second tick should NOT reconcile it (it stays Active)
        ReconciliationStubWorkerOrchestrator orchestrator = new(status: null);
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

    internal sealed class ReconciliationStubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public int GetStatusCallCount { get; private set; }

        public Task<Result<string>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Fail(new Error("Test.NoDispatch", "No dispatch in reconciliation tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        {
            GetStatusCallCount++;
            return Task.FromResult(status);
        }

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
