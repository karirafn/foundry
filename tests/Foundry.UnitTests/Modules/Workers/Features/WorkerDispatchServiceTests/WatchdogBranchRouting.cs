using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
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
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true);
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
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false);
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
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true);
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
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false);
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
    public async Task WhenOrphanReconcileAndCommitsCheckFails_RunRemainsActive()
    {
        // Arrange — transient provider error: resolver returns Indeterminate → no transition, no event
        SeedActiveRun("container-orphan-check-fails", branchName: "feat/12-orphan-check-fails");
        FailingBranchCommitsCheckQueries postExitQueries = new();
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run stays active and no failed event is dispatched
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
        capturingDispatcher.Captured.OfType<WorkerRunFailed>().ShouldBeEmpty();
    }

    // ─── Container-not-found path ────────────────────────────────────────────────

    [Fact]
    public async Task WhenContainerNotFoundDuringMonitoringAndBranchHasCommits_DispatchesFailedEventWithBranchName()
    {
        // Arrange — container returns null status; reconcile + monitor paths both use ResolveBranchForFailureAsync
        SeedActiveRun("container-missing-with-commits", branchName: "feat/13-missing-commits");
        StubPostExitProviderQueries postExitQueries = new(hasCommits: true);
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
        StubPostExitProviderQueries postExitQueries = new(hasCommits: false);
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
    public async Task WhenContainerNotFoundDuringMonitoringAndCommitsCheckFails_RunRemainsActive()
    {
        // Arrange — transient provider error: resolver returns Indeterminate → no transition, no event
        SeedActiveRun("container-missing-check-fails", branchName: "feat/15-missing-check-fails");
        FailingBranchCommitsCheckQueries postExitQueries = new();
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            postExitProviderQueries: postExitQueries,
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run stays active and no failed event is dispatched
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
        capturingDispatcher.Captured.OfType<WorkerRunFailed>().ShouldBeEmpty();
    }

    // ─── Category token assertions ───────────────────────────────────────────────

    [Fact]
    public async Task WhenOrphanReconcile_DispatchesFailedEventWithNonZeroExitCategory()
    {
        // Arrange — resolver maps null-exit with no MR and no commits → NonZeroExit(-1)
        SeedActiveRun("container-orphan-category");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new NullStatusOrchestrator(),
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("non_zero_exit");
    }

    [Fact]
    public async Task WhenTimeout_DispatchesFailedEventWithTimedOutCategory()
    {
        // Arrange
        SeedActiveRun("container-timeout-category");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(
            orchestrator: new RunningStubOrchestrator(),
            settingsQueries: new StubGlobalSettingsQueries(timeoutMinutes: 0),
            integrationEventDispatcher: capturingDispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("timed_out");
    }

    [Fact]
    public async Task WhenContainerNotFoundDuringMonitoring_DispatchesFailedEventWithNonZeroExitCategory()
    {
        // Arrange — second tick where _reconciled=true, monitor path hits null status.
        // Resolver maps null-exit with no MR and no commits → NonZeroExit(-1).
        SeedActiveRun("container-missing-category");
        CapturingIntegrationEventDispatcher capturingDispatcher = new();

        // Build a sut where reconciliation has already run (_reconciled=true) so the monitor
        // path fires on the second tick.
        WorkerDispatchService sut = BuildService(
            orchestrator: new SecondTickNullStatusOrchestrator(),
            integrationEventDispatcher: capturingDispatcher);

        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken); // reconcile tick (status: running)
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken); // monitor tick (status: null)

        // Assert
        WorkerRunFailed failedEvent = capturingDispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("non_zero_exit");
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Orchestrator that reports the container as running (used for the timeout path).
    /// </summary>
    private sealed class RunningStubOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

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

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

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
    /// Returns running status on the first call, null on subsequent calls.
    /// Used to simulate the container-not-found path on the monitoring tick (after reconciliation).
    /// </summary>
    private sealed class SecondTickNullStatusOrchestrator : IWorkerOrchestrator
    {
        private int _callCount;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
        {
            _callCount++;
            WorkerStatusProbe probe = _callCount == 1
                ? new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null))
                : new WorkerStatusProbe.NotFound();
            return Task.FromResult(probe);
        }

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

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }
}
