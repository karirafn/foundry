using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class TimeoutDetection : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        TimeoutStubWorkerOrchestrator orchestrator,
        int timeoutMinutes = 120)
    {
        WorkerOptions options = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = timeoutMinutes,
        };

        // Delegates to base.BuildService — accesses inherited instance state.
        return base.BuildService(orchestrator, options);
    }

    [Fact]
    public async Task WhenRunHasExceededTimeout_StopsContainerAndTransitionsToFailedRun()
    {
        // Arrange — use TimeoutMinutes = 0 so any run started in the past is immediately timed out
        SeedActiveRun();
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run transitioned to FailedRun with TimedOut reason
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.TimedOut>();
    }

    [Fact]
    public async Task WhenRunHasExceededTimeout_CallsStopOnOrchestrator()
    {
        // Arrange — use TimeoutMinutes = 0 so any run started in the past is immediately timed out
        SeedActiveRun("container-timeout-test");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — orchestrator.StopAsync was called with the container ID
        orchestrator.StoppedContainerId.ShouldBe("container-timeout-test");
    }

    [Fact]
    public async Task WhenRunHasNotExceededTimeout_DoesNotTransitionRun()
    {
        // Arrange — use a very large timeout so no run will time out
        SeedActiveRun();
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 99999);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run remains active
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    internal sealed class TimeoutStubWorkerOrchestrator(bool isRunning) : IWorkerOrchestrator
    {
        public string? StoppedContainerId { get; private set; }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in timeout tests")));

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
        {
            StoppedContainerId = containerId;
            return Task.CompletedTask;
        }

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: isRunning, ExitCode: null, FinishedAt: null));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
