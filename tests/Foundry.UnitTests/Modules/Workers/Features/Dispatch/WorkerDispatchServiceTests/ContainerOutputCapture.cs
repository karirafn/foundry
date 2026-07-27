using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class ContainerOutputCapture : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenContainerExitsWithNonZero_ContainerOutputStoredOnFailedRun()
    {
        // Arrange
        SeedActiveRun("container-nonzero-output");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        LogCapturingStub orchestrator = new(status: failedStatus, logs: "error: something went wrong");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBe("error: something went wrong");
    }

    [Fact]
    public async Task WhenContainerNotFound_ContainerOutputIsNull()
    {
        // Arrange — seed ActiveRun; first tick: reconciliation sees null status → FailedRun
        SeedActiveRun("container-not-found-output");
        LogCapturingStub orchestrator = new(status: null, logs: "should-not-be-captured");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBeNull();
    }

    [Fact]
    public async Task WhenGetLogsAsyncThrows_ContainerOutputIsNullAndTransitionSucceeds()
    {
        // Arrange
        SeedActiveRun("container-logs-throw");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        LogCapturingStub orchestrator = new(status: failedStatus, logs: null, getLogsThrows: true);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — must not throw even though GetLogsAsync throws
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — transition still succeeded; ContainerOutput is null
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOrphanedRunReconciled_ContainerOutputIsNull()
    {
        // Arrange — first tick: reconciliation finds container not found → fails as orphaned
        SeedActiveRun("orphaned-container-output");
        LogCapturingStub orchestrator = new(status: null, logs: "should-not-be-captured");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerOutputExceedsMaxLength_OutputIsTruncatedBeforeStore()
    {
        // Arrange
        SeedActiveRun("container-output-overflow");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        string oversizedOutput = new('x', 65537);
        LogCapturingStub orchestrator = new(status: failedStatus, logs: oversizedOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldNotBeNull();
        failedRun.ContainerOutput!.Length.ShouldBeLessThanOrEqualTo(65536);
    }

    [Fact]
    public async Task WhenContainerExitsWithNonZero_GetLogsAsyncCalledBeforeStop()
    {
        // Arrange
        SeedActiveRun("container-log-order");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        LogCapturingStub orchestrator = new(status: failedStatus, logs: "exit output");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — logs captured before container stopped
        orchestrator.GetLogsCallCount.ShouldBe(1);
        orchestrator.GetLogsCalledBeforeStop.ShouldBeTrue();
    }

    private sealed class LogCapturingStub : IWorkerOrchestrator
    {
        private readonly WorkerStatus? _status;
        private readonly string? _logs;
        private readonly bool _getLogsThrows;
        private bool _stopCalled;

        public int GetLogsCallCount { get; private set; }
        public bool GetLogsCalledBeforeStop { get; private set; }

        public LogCapturingStub(WorkerStatus? status, string? logs, bool getLogsThrows = false)
        {
            _status = status;
            _logs = logs;
            _getLogsThrows = getLogsThrows;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in log capture tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            _stopCalled = true;
            return Task.CompletedTask;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(_status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(_status));

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
            GetLogsCalledBeforeStop = !_stopCalled;

            if (_getLogsThrows)
            {
                throw new InvalidOperationException("GetLogs failed");
            }

            return Task.FromResult(_logs);
        }

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
