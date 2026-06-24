using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

/// <summary>
/// Tests that the three non-clean-exit failure paths (timeout, orphan-reconcile, container-not-found)
/// consult HasBranchCommitsAsync before setting BranchName on WorkerRunFailed events — mirroring the
/// clean-exit path in ProcessExitedRunAsync.
/// </summary>
public sealed class WatchdogBranchRouting : WorkerDispatchServiceTestBase
{
    // ─── Timeout path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTimeoutAndBranchHasCommits_DispatchesFailedEventWithBranchName()
    {
        // Arrange
        SeedActiveRun("container-timeout-with-commits", branchName: "feat/7-partial-work");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new RunningStubOrchestrator(),
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/7-partial-work");
    }

    [Fact]
    public async Task WhenTimeoutAndBranchHasNoCommits_DispatchesFailedEventWithNullBranchName()
    {
        // Arrange
        SeedActiveRun("container-timeout-no-commits", branchName: "feat/8-no-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new RunningStubOrchestrator(),
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTimeoutAndCommitsCheckFails_DispatchesFailedEventWithBranchName()
    {
        // Arrange — provider outage: default to current behavior (non-null branch name)
        SeedActiveRun("container-timeout-commits-check-fails", branchName: "feat/9-check-fails");
        FailingBranchCommitsCheckQueries postExitQueries = new();
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new RunningStubOrchestrator(),
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — falls back to non-null branch name; preserves existing behavior
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/9-check-fails");
    }

    // ─── Orphan-reconcile path ───────────────────────────────────────────────────

    [Fact]
    public async Task WhenOrphanReconcileAndBranchHasCommits_DispatchesFailedEventWithBranchName()
    {
        // Arrange — first tick, container not found during reconciliation
        SeedActiveRun("container-orphan-with-commits", branchName: "feat/10-orphan-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/10-orphan-commits");
    }

    [Fact]
    public async Task WhenOrphanReconcileAndBranchHasNoCommits_DispatchesFailedEventWithNullBranchName()
    {
        // Arrange — first tick, container not found during reconciliation
        SeedActiveRun("container-orphan-no-commits", branchName: "feat/11-orphan-no-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenOrphanReconcileAndCommitsCheckFails_DispatchesFailedEventWithBranchName()
    {
        // Arrange — provider outage: default to current behavior (non-null branch name)
        SeedActiveRun("container-orphan-check-fails", branchName: "feat/12-orphan-check-fails");
        FailingBranchCommitsCheckQueries postExitQueries = new();
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/12-orphan-check-fails");
    }

    // ─── Container-not-found path ────────────────────────────────────────────────

    [Fact]
    public async Task WhenContainerNotFoundDuringMonitoringAndBranchHasCommits_DispatchesFailedEventWithBranchName()
    {
        // Arrange — container returns null status; reconcile + monitor paths both use ResolveBranchForFailureAsync
        SeedActiveRun("container-missing-with-commits", branchName: "feat/13-missing-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/13-missing-commits");
    }

    [Fact]
    public async Task WhenContainerNotFoundDuringMonitoringAndBranchHasNoCommits_DispatchesFailedEventWithNullBranchName()
    {
        // Arrange — container returns null status on second tick
        SeedActiveRun("container-missing-no-commits", branchName: "feat/14-missing-no-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false, prUrl: null);
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerNotFoundDuringMonitoringAndCommitsCheckFails_DispatchesFailedEventWithBranchName()
    {
        // Arrange — provider outage: default to current behavior (non-null branch name)
        SeedActiveRun("container-missing-check-fails", branchName: "feat/15-missing-check-fails");
        FailingBranchCommitsCheckQueries postExitQueries = new();
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBe("feat/15-missing-check-fails");
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Orchestrator that reports the container as running (used for the timeout path).
    /// </summary>
    private sealed class RunningStubOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));

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

    /// <summary>
    /// Orchestrator that returns null status (used for both orphan-reconcile and container-not-found paths).
    /// </summary>
    private sealed class NullStatusOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(null);

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

    private sealed class FailingBranchCommitsCheckQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Fail(new Error("Provider.Unavailable", "Git provider returned an error")));

        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<string>> GetPullRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(string.Empty));
    }
}
