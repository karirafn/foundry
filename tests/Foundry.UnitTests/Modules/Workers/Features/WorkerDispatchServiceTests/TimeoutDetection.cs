using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

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
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.TimedOut>();
    }

    [Fact]
    public async Task WhenRunHasExceededTimeout_CallsStopContainerAsyncOnOrchestrator()
    {
        // Arrange — use TimeoutMinutes = 0 so any run started in the past is immediately timed out
        SeedActiveRun("container-timeout-test");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — orchestrator.StopContainerAsync was called with the container ID
        orchestrator.StopContainerCalledWith.ShouldBe("container-timeout-test");
    }

    [Fact]
    public async Task WhenRunTimesOut_ContainerOutputStoredOnFailedRun()
    {
        // Arrange
        SeedActiveRun("container-timeout-output");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true, logs: "timeout output captured");
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBe("timeout output captured");
    }

    [Fact]
    public async Task WhenRunTimesOut_CallsStopContainerAsync()
    {
        // Arrange
        SeedActiveRun("container-timeout-stop");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopContainerCalledWith.ShouldBe("container-timeout-stop");
    }

    [Fact]
    public async Task WhenRunTimesOut_CallsRemoveContainerAsync()
    {
        // Arrange
        SeedActiveRun("container-timeout-remove");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.RemoveContainerCalledWith.ShouldBe("container-timeout-remove");
    }

    [Fact]
    public async Task WhenRunTimesOut_CapturesLogsAfterStoppingContainer()
    {
        // Arrange
        SeedActiveRun("container-timeout-order");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true, logs: "some output");
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — logs captured after stop
        orchestrator.GetLogsCallCount.ShouldBe(1);
        orchestrator.GetLogsCalledAfterStopContainer.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRunTimesOut_RemoveCalledAfterGetLogs()
    {
        // Arrange
        SeedActiveRun("container-timeout-remove-order");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true, logs: "some output");
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — remove called after log capture
        orchestrator.RemoveCalledAfterGetLogs.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRemoveContainerAsyncThrows_TransitionSucceedsAndDoesNotThrow()
    {
        // Arrange
        SeedActiveRun("container-timeout-remove-throws");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true, removeContainerThrows: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act — must not throw even though RemoveContainerAsync throws
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — transition still succeeded
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenStopContainerAsyncThrows_StillCapturesLogsAndTransitions()
    {
        // Arrange
        SeedActiveRun("container-timeout-stop-throws");
        TimeoutStubWorkerOrchestrator orchestrator = new(isRunning: true, logs: "output after stop failure", stopContainerThrows: true);
        WorkerDispatchService sut = BuildService(orchestrator, timeoutMinutes: 0);

        // Act — must not throw even though StopContainerAsync throws
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — logs still captured and transition succeeded
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBe("output after stop failure");
        orchestrator.GetLogsCallCount.ShouldBe(1);
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
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    internal sealed class TimeoutStubWorkerOrchestrator(
        bool isRunning,
        string? logs = null,
        bool stopContainerThrows = false,
        bool removeContainerThrows = false) : IWorkerOrchestrator
    {
        private readonly List<string> _callOrder = [];

        public string? StoppedContainerId { get; private set; }
        public string? StopContainerCalledWith { get; private set; }
        public string? RemoveContainerCalledWith { get; private set; }
        public int GetLogsCallCount { get; private set; }
        public bool GetLogsCalledAfterStopContainer { get; private set; }
        public bool RemoveCalledAfterGetLogs { get; private set; }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in timeout tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
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

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
        {
            GetLogsCallCount++;
            GetLogsCalledAfterStopContainer = _callOrder.Contains("stop_container");
            _callOrder.Add("get_logs");
            return Task.FromResult(logs);
        }

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
        {
            if (stopContainerThrows)
            {
                throw new InvalidOperationException("StopContainer failed");
            }

            StopContainerCalledWith = containerId;
            _callOrder.Add("stop_container");
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
        {
            if (removeContainerThrows)
            {
                throw new InvalidOperationException("RemoveContainer failed");
            }

            RemoveContainerCalledWith = containerId;
            RemoveCalledAfterGetLogs = _callOrder.Contains("get_logs");
            _callOrder.Add("remove_container");
            return Task.CompletedTask;
        }
    }
}
