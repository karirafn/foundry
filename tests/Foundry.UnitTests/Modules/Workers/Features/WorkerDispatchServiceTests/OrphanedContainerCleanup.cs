using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class OrphanedContainerCleanup : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenDockerReturnsContainerWithNoDbRecord_StopsContainer()
    {
        // Arrange
        WorkerRunId unknownRunId = WorkerRunId.New();
        ContainerId containerId = ContainerId.From("unknown-container-abc");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers =
            [(containerId, unknownRunId)];
        OrphanedContainerCleanupStub orchestrator = new(containers: containers);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAsyncCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDockerReturnsContainerMatchingActiveRun_DoesNotStopContainer()
    {
        // Arrange
        ActiveRun activeRun = SeedActiveRun("active-container-xyz");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers =
            [(activeRun.ContainerId, activeRun.Id)];
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        WorkerOptions options = new()
        {
            Image = "test-image:latest",
        };
        StubGlobalSettingsQueries settingsQueries = new(maxConcurrent: 3, timeoutMinutes: 9999);
        OrphanedContainerCleanupStub orchestrator = new(containers: containers, status: runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator, workerOptions: options, settingsQueries: settingsQueries);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAsyncCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDockerReturnsContainerMatchingCompletedRun_StopsContainer()
    {
        // Arrange
        CompletedRun completedRun = SeedCompletedRun();
        ContainerId containerId = ContainerId.From("completed-container");
        IReadOnlyList<(ContainerId, WorkerRunId)> containers =
            [(containerId, completedRun.Id)];
        OrphanedContainerCleanupStub orchestrator = new(containers: containers);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAsyncCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDockerScanThrows_LogsWarningAndContinues()
    {
        // Arrange
        OrphanedContainerCleanupStub orchestrator = new(listThrows: true);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — second tick skips reconciliation (_reconciled=true), proving execution continued
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        orchestrator.StopAsyncCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDockerReturnsNoContainers_DoesNotStop()
    {
        // Arrange
        IReadOnlyList<(ContainerId, WorkerRunId)> containers = [];
        OrphanedContainerCleanupStub orchestrator = new(containers: containers);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        orchestrator.StopAsyncCallCount.ShouldBe(0);
    }

    private CompletedRun SeedCompletedRun()
    {
        using FoundryDbContext db = CreateDbContext();
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun activeRun = starting.Activate(ContainerId.From("completed-container"));
        CompletedRun completedRun = activeRun.Complete(exitCode: 0, branchName: null, pullRequestUrl: null);
        db.Set<WorkerRun>().Add(completedRun);
        db.SaveChanges();
        return completedRun;
    }

    private sealed class OrphanedContainerCleanupStub : IWorkerOrchestrator
    {
        private readonly IReadOnlyList<(ContainerId, WorkerRunId)> _containers;
        private readonly WorkerStatus? _status;
        private readonly bool _listThrows;

        public int StopAsyncCallCount { get; private set; }

        public List<string> StoppedContainerIds { get; } = [];

        public OrphanedContainerCleanupStub(
            IReadOnlyList<(ContainerId, WorkerRunId)>? containers = null,
            WorkerStatus? status = null,
            bool listThrows = false)
        {
            _containers = containers ?? [];
            _status = status;
            _listThrows = listThrows;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "No dispatch")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            StopAsyncCallCount++;
            StoppedContainerIds.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult(_status);

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
        {
            if (_listThrows)
            {
                throw new InvalidOperationException("Docker daemon unavailable");
            }

            return Task.FromResult(_containers);
        }

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
