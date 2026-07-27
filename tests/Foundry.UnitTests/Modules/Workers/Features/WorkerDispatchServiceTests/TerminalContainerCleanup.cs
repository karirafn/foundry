using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class TerminalContainerCleanup : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenContainerExitsWithZero_StopsContainerAfterTransition()
    {
        // Arrange
        SeedActiveRun("container-exit-zero");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        TerminalCleanupStub orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
        orchestrator.StopAsyncCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenContainerExitsWithNonZero_StopsContainerAfterTransition()
    {
        // Arrange
        SeedActiveRun("container-exit-nonzero");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 2, FinishedAt: DateTimeOffset.UtcNow);
        TerminalCleanupStub orchestrator = new(status: exitedStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
        orchestrator.StopAsyncCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenContainerNotFound_StopsContainerAfterTransition()
    {
        // Arrange — seed ActiveRun; reconciliation on first tick finds container missing → FailedRun + TryStopAsync
        SeedActiveRun("container-not-found");
        TerminalCleanupStub orchestrator = new(status: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAsyncCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStopAsyncThrowsOnTerminalTransition_TransitionAlreadySucceeded()
    {
        // Arrange
        SeedActiveRun("container-stop-throws");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        TerminalCleanupStub orchestrator = new(status: exitedStatus, stopThrows: true);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act — must not throw even though StopAsync throws
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — transition succeeded before the failed stop
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    private sealed class TerminalCleanupStub : IWorkerOrchestrator
    {
        private readonly WorkerStatus? _status;
        private readonly bool _stopThrows;

        public int StopAsyncCallCount { get; private set; }

        public TerminalCleanupStub(WorkerStatus? status = null, bool stopThrows = false)
        {
            _status = status;
            _stopThrows = stopThrows;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            StopAsyncCallCount++;
            if (_stopThrows)
            {
                throw new InvalidOperationException("Stop failed");
            }

            return Task.CompletedTask;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(_status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(_status));

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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
