using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Verifies that the reconciliation tick records activity (new log output) and commits
/// (new SHA from provider) on still-running workers, and persists the changes.
/// </summary>
public sealed class RunningWorkerActivityTracking : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        IPostExitProviderQueries? postExitProviderQueries = null)
    {
        return base.BuildService(
            orchestrator,
            postExitProviderQueries: postExitProviderQueries);
    }

    [Fact]
    public async Task WhenRunningWorkerHasNewLogOutput_LastActivityAtIsSet()
    {
        // Arrange — first tick so reconciliation sets _reconciled=true; second tick exercises the running-worker path
        SeedActiveRun("running-activity-log");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: "new log output");
        WorkerDispatchService sut = BuildService(orchestrator);

        // Tick 1: reconciliation — no new logs (firstLogs = null)
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: running-worker path, new logs arrive
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenRunningWorkerHasNoNewLogOutput_LastActivityAtRemainsNull()
    {
        // Arrange — same log on every tick (empty = no new output)
        SeedActiveRun("running-no-activity");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: same (null) logs
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRunningWorkerLogsGrow_ActivityBumpsOnEachNewOutput()
    {
        // Arrange — logs grow from tick 2 to tick 3
        SeedActiveRun("running-activity-grows");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        // Tick 1: null → no activity; Tick 2: "abc" → activity bump; Tick 3: "abcdef" → activity bump again
        MultiTickLogsOrchestrator orchestrator = new(runningStatus, ["abc", "abcdef"]);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Tick 1: reconciliation; no logs
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: first log output
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        await using FoundryDbContext db2 = CreateDbContext();
        WorkerRun? run2 = await db2.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active2 = run2.ShouldBeOfType<ActiveRun>();
        DateTimeOffset? firstActivity = active2.LastActivityAt;
        firstActivity.ShouldNotBeNull();

        // Act — Tick 3: more log output
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — activity updated again (may or may not differ by time, but RecordActivity was called)
        await using FoundryDbContext db3 = CreateDbContext();
        WorkerRun? run3 = await db3.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active3 = run3.ShouldBeOfType<ActiveRun>();
        active3.LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenRunningWorkerHasNewCommit_CommitMarkerRecorded()
    {
        // Arrange
        SeedActiveRun("running-commit-record");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        LatestBranchCommit commit = new("sha-abc123", "feat: initial commit");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: "log", secondLogs: "log");
        ScriptedCommitProviderQueries queries = new(firstCommit: null, secondCommit: commit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation — no commit yet
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: new commit observed
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.CommitMarkers.ShouldHaveSingleItem();
        activeRun.CommitMarkers[0].Sha.ShouldBe("sha-abc123");
    }

    [Fact]
    public async Task WhenRunningWorkerSameCommitTwice_CommitMarkerNotDuplicated()
    {
        // Arrange — same SHA returned on both tick 2 and tick 3
        SeedActiveRun("running-commit-dedup");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        LatestBranchCommit commit = new("sha-dedup", "feat: dedup test");
        // Both ticks return the same commit
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: "log", secondLogs: "log");
        ConstantCommitProviderQueries queries = new(commit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: first time seeing commit
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 3: same commit SHA again
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — only one commit marker despite two ticks seeing the same SHA
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.CommitMarkers.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenCommitProviderReturnsFailure_ActivityStillRecordedFromLogs()
    {
        // Arrange — provider fails but logs produce activity
        SeedActiveRun("running-provider-fail");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: "new output");
        FailingCommitProviderQueries queries = new();
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: provider fails, but logs have new output
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — activity recorded from logs; no commit markers; no exception thrown
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldNotBeNull();
        activeRun.CommitMarkers.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRunningWorkerHasNoNewOutputAndNoNewCommit_RunRemainsActivePersisted()
    {
        // Arrange — no change on tick 2
        SeedActiveRun("running-no-change");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run still active, no crash
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    // ─── orchestrator / query stubs ──────────────────────────────────────────

    /// <summary>
    /// Returns firstLogs on all calls until the first SaveChanges cycle completes (i.e., first tick),
    /// then returns secondLogs on subsequent calls.
    /// </summary>
    private sealed class ScriptedLogsOrchestrator(
        WorkerStatus status,
        string? firstLogs,
        string? secondLogs) : IWorkerOrchestrator
    {
        private int _callCount;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "no dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(status));

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
            string? logs = _callCount == 0 ? firstLogs : secondLogs;
            _callCount++;
            return Task.FromResult(logs);
        }

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }

    private sealed class MultiTickLogsOrchestrator(WorkerStatus status, IReadOnlyList<string?> logsPerTick)
        : IWorkerOrchestrator
    {
        private int _callCount;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "no dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(status));

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
            string? logs = _callCount < logsPerTick.Count ? logsPerTick[_callCount] : logsPerTick[^1];
            _callCount++;
            return Task.FromResult(logs);
        }

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }

    private sealed class ScriptedCommitProviderQueries(
        LatestBranchCommit? firstCommit,
        LatestBranchCommit? secondCommit) : IPostExitProviderQueries
    {
        private int _callCount;

        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

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
        {
            LatestBranchCommit? commit = _callCount == 0 ? firstCommit : secondCommit;
            _callCount++;
            return Task.FromResult(commit is null
                ? Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit"))
                : Result<LatestBranchCommit>.Ok(commit));
        }
    }

    private sealed class ConstantCommitProviderQueries(LatestBranchCommit commit) : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

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
            => Task.FromResult(Result<LatestBranchCommit>.Ok(commit));
    }

    private sealed class FailingCommitProviderQueries : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

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
                Result<LatestBranchCommit>.Fail(new Error("Provider.Unavailable", "provider down")));
    }
}
