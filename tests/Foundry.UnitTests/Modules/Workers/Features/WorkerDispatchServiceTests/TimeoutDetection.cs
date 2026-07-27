using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

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
        };

        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: 3, timeoutMinutes: timeoutMinutes);

        // Delegates to base.BuildService — accesses inherited instance state.
        return base.BuildService(orchestrator, options, settingsQueries: settingsQueries);
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
    public async Task WhenExitedContainerPastTimeoutHasMergedMr_TransitionsToCompletedRun()
    {
        // Arrange — container already exited (IsRunning: false) but past the timeout wall clock.
        // MR-state-first must win: a merged PR → Completed, not the timeout ContinuableFailure/Failure.
        const string prUrl = "https://github.com/owner/repo/pull/99";
        SeedActiveRun("container-exited-past-timeout");
        ExitedStubOrchestrator orchestrator = new(exitCode: 0);
        StubPostExitProviderQueries postExitQueries = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Merged, prUrl));
        WorkerDispatchService sut = base.BuildService(
            orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            postExitProviderQueries: postExitQueries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — MR-state-first: merged PR → CompletedRun, not FailedRun (timeout)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
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

    /// <summary>
    /// Orchestrator that reports the container as already exited with the given exit code.
    /// Used to verify that an exited container is always resolved via MR-state-first,
    /// even when the wall-clock timeout has passed.
    /// </summary>
    private sealed class ExitedStubOrchestrator(int exitCode) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow)));

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
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }

    internal sealed class TimeoutStubWorkerOrchestrator(
        bool isRunning,
        string? logs = null,
        bool stopContainerThrows = false,
        bool removeContainerThrows = false) : IWorkerOrchestrator
    {
        private readonly List<string> _callOrder = [];

        public string? StopContainerCalledWith { get; private set; }
        public string? RemoveContainerCalledWith { get; private set; }
        public int GetLogsCallCount { get; private set; }
        public bool GetLogsCalledAfterStopContainer { get; private set; }
        public bool RemoveCalledAfterGetLogs { get; private set; }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in timeout tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
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

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: isRunning, ExitCode: null, FinishedAt: null)));

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
