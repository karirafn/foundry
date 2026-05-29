using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class MonitorActiveRuns : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        MonitoringStubWorkerOrchestrator orchestrator,
        WorkerOptions? workerOptions = null)
    {
        WorkerOptions options = workerOptions ?? new WorkerOptions
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = 120,
        };

        // Delegates to base.BuildService — accesses inherited instance state.
        return base.BuildService(orchestrator, options);
    }

    [Fact]
    public async Task WhenContainerDisappearsAfterReconciliation_TransitionsToFailedRunWithContainerError()
    {
        // Arrange — seed the run after the first tick so reconciliation does not process it;
        // on the second tick the monitoring loop finds the container missing
        MonitoringStubWorkerOrchestrator orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // First tick: no active runs exist yet, so reconciliation has nothing to process
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed the active run after reconciliation has already completed
        SeedActiveRun("missing-container");

        // Act — second tick: reconciliation skipped; monitoring sees null status
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldBe("Container not found");
    }

    [Fact]
    public async Task WhenContainerExitsWithZero_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("exited-container");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        CompletedRun completedRun = run.ShouldBeOfType<CompletedRun>();
        completedRun.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task WhenContainerExitsWithNonZero_TransitionsToFailedRunWithNonZeroExit()
    {
        // Arrange
        SeedActiveRun("failed-container");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: failedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.NonZeroExit nonZeroExit = failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task WhenContainerStillRunning_RunRemainsActive()
    {
        // Arrange
        SeedActiveRun("running-container");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        MonitoringStubWorkerOrchestrator orchestrator = new(status: runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    internal sealed class MonitoringStubWorkerOrchestrator(WorkerStatus? status) : IWorkerOrchestrator
    {
        public string? LastStoppedContainerId { get; private set; }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in monitor tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
        {
            LastStoppedContainerId = containerId;
            return Task.CompletedTask;
        }

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult(status);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
