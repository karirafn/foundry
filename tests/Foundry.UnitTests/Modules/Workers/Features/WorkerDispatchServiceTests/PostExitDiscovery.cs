using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class PostExitDiscovery : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        IPostExitProviderQueries postExitProviderQueries,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        MonitoringStubWorkerOrchestrator orchestrator = new(exitedStatus);
        return base.BuildService(
            orchestrator,
            postExitProviderQueries: postExitProviderQueries,
            integrationEventDispatcher: integrationEventDispatcher);
    }

    [Fact]
    public async Task WhenExitCodeZeroAndHasCommitsAndPrFound_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("container-success-pr");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(
            hasCommits: true,
            prUrl: "https://github.com/owner/repo/pull/42");
        WorkerDispatchService sut = BuildService(queries, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenExitCodeZeroAndHasCommitsAndPrFound_DispatchesCompletedEventWithPrUrl()
    {
        // Arrange
        SeedActiveRun("container-success-pr-dispatch");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(
            hasCommits: true,
            prUrl: "https://github.com/owner/repo/pull/42");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(queries, exitedStatus, capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunCompleted completedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunCompleted>()
            .ShouldHaveSingleItem();
        completedEvent.ShouldSatisfyAllConditions(
            () => completedEvent.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => completedEvent.BranchName.ShouldNotBeNull());
    }

    [Fact]
    public async Task WhenExitCodeZeroAndNoCommits_TransitionsToCompletedRun()
    {
        // Arrange
        SeedActiveRun("container-success-no-commits");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: false, prUrl: null);
        WorkerDispatchService sut = BuildService(queries, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<CompletedRun>();
    }

    [Fact]
    public async Task WhenExitCodeZeroAndNoCommits_DispatchesCompletedEventWithNullPrUrl()
    {
        // Arrange
        SeedActiveRun("container-no-commits-dispatch");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: false, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(queries, exitedStatus, capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunCompleted completedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunCompleted>()
            .ShouldHaveSingleItem();
        completedEvent.PullRequestUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenExitCodeZeroAndHasCommitsAndNoPrAfterRetries_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("container-success-no-pr");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: true, prUrl: null);
        WorkerDispatchService sut = BuildService(queries, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenExitCodeZeroAndHasCommitsAndNoPrAfterRetries_DispatchesFailedEventWithBranchName()
    {
        // Arrange
        SeedActiveRun("container-no-pr-dispatch", branchName: "feat/42-my-issue");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: true, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(queries, exitedStatus, capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/42-my-issue");
    }

    [Fact]
    public async Task WhenNonZeroExitAndHasCommits_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("container-nonzero-with-commits");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: true, prUrl: null);
        WorkerDispatchService sut = BuildService(queries, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenNonZeroExitAndHasCommits_DispatchesFailedEventWithBranchName()
    {
        // Arrange
        SeedActiveRun("container-nonzero-commits-dispatch", branchName: "feat/10-partial-work");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: true, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(queries, exitedStatus, capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/10-partial-work");
    }

    [Fact]
    public async Task WhenNonZeroExitAndNoCommits_TransitionsToFailedRun()
    {
        // Arrange
        SeedActiveRun("container-nonzero-no-commits");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: false, prUrl: null);
        WorkerDispatchService sut = BuildService(queries, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<FailedRun>();
    }

    [Fact]
    public async Task WhenNonZeroExitAndNoCommits_DispatchesFailedEventWithNullBranchName()
    {
        // Arrange
        SeedActiveRun("container-nonzero-nocommits-dispatch");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        IPostExitProviderQueries queries = new StubPostExitProviderQueries(hasCommits: false, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(queries, exitedStatus, capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBeNull();
    }

    private sealed class MonitoringStubWorkerOrchestrator(WorkerStatus exitedStatus) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in post-exit tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(exitedStatus);

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
}
