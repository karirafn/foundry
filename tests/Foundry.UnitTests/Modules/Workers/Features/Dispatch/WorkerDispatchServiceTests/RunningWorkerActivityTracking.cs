using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Verifies that the reconciliation tick records activity from container liveness (not log growth)
/// on still-running workers; no commit count is observed while a run works (ADR 0100).
/// </summary>
public sealed class RunningWorkerActivityTracking : WorkerDispatchServiceTestBase
{
    private DispatchAndPublishTicker BuildService(
        IWorkerOrchestrator orchestrator,
        IPostExitProviderQueries? postExitProviderQueries = null,
        IDomainEventDispatcher? domainEventDispatcher = null,
        CapturingLogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        return base.BuildService(
            orchestrator,
            postExitProviderQueries: postExitProviderQueries,
            domainEventDispatcher: domainEventDispatcher,
            logger: logger,
            timeProvider: timeProvider);
    }

    [Fact]
    public async Task WhenContainerIsAlive_LastActivityAtIsSet()
    {
        // Arrange — container is running; logs return null (no output), but liveness is from container status
        SeedActiveRun("running-activity-alive");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: running-worker path — container is alive, no log output
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — activity set from container-alive signal (not log growth)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenContainerIsAliveWithNoLogOutput_LastActivityAtIsSet()
    {
        // Arrange — no log output at all, but container is provably running
        SeedActiveRun("running-no-logs");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        // ScriptedLogsOrchestrator returns null for both ticks — no log output
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: container alive, zero log output
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — LastActivityAt is set from container-alive, not from logs
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenContainerObservedMultipleTimes_LastActivityAtAdvancesEachTick()
    {
        // Arrange — container alive on every tick; stepping time ensures each tick gets a strictly
        // greater timestamp so the assertion is deterministic regardless of wall-clock resolution.
        SeedActiveRun("running-activity-multi");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        SteppingTimeProvider timeProvider = new();
        DispatchAndPublishTicker sut = BuildService(orchestrator, timeProvider: timeProvider);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: first activity observation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        await using FoundryDbContext db2 = CreateDbContext();
        WorkerRun? run2 = await db2.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active2 = run2.ShouldBeOfType<ActiveRun>();
        DateTimeOffset activityAfterTick2 = active2.LastActivityAt.ShouldNotBeNull();

        // Act — Tick 3: second activity observation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — LastActivityAt has strictly advanced compared to tick 2
        await using FoundryDbContext db3 = CreateDbContext();
        WorkerRun? run3 = await db3.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active3 = run3.ShouldBeOfType<ActiveRun>();
        DateTimeOffset activityAfterTick3 = active3.LastActivityAt.ShouldNotBeNull();
        activityAfterTick3.ShouldBeGreaterThan(activityAfterTick2);
    }

    [Fact]
    public async Task WhenRunningWorkerHasNoNewOutputAndNoNewCommit_RunRemainsActivePersisted()
    {
        // Arrange — no change on tick 2
        SeedActiveRun("running-no-change");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — run still active, no crash
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        run.ShouldBeOfType<ActiveRun>();
    }

    // ─── push-webhook driven compares (issue #660) ────────────────────────────

    // ─── orchestrator / query stubs ──────────────────────────────────────────

    /// <summary>Returns <see cref="Summary"/> for every compare and counts the compares.</summary>
    private sealed class CountingCommitProviderQueries(BranchCommitSummary summary) : IPostExitProviderQueries
    {
        public BranchCommitSummary Summary { get; set; } = summary;

        public int CompareCount { get; private set; }

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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            CompareCount++;
            return Task.FromResult(Result<BranchCommitSummary>.Ok(Summary));
        }
    }

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

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

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


        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ScriptedCommitProviderQueries(
        BranchCommitSummary? firstSummary,
        BranchCommitSummary? secondSummary) : IPostExitProviderQueries
    {
        private int _callCount;

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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            BranchCommitSummary? summary = _callCount == 0 ? firstSummary : secondSummary;
            _callCount++;
            return Task.FromResult(summary is null
                ? Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit"))
                : Result<BranchCommitSummary>.Ok(summary));
        }
    }

    private sealed class ConstantCommitProviderQueries(BranchCommitSummary summary) : IPostExitProviderQueries
    {
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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchCommitSummary>.Ok(summary));
    }

    private sealed class FailingCommitProviderQueries : IPostExitProviderQueries
    {
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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.Unavailable", "provider down")));
    }

    /// <summary>
    /// Returns <paramref name="firstCommit"/> on the first call (tick 1), then NotFound on all
    /// subsequent calls — simulates a branch that existed, pushed commits, then was deleted.
    /// </summary>
    private sealed class SuccessThenNotFoundCommitProviderQueries(BranchCommitSummary firstCommit)
        : IPostExitProviderQueries
    {
        private int _callCount;

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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            if (_callCount == 0)
            {
                _callCount++;
                return Task.FromResult(Result<BranchCommitSummary>.Ok(firstCommit));
            }

            _callCount++;
            return Task.FromResult(
                Result<BranchCommitSummary>.Fail(
                    new Error("Provider.NotFound", "Branch not found") { Kind = ErrorKind.NotFound }));
        }
    }

    /// <summary>
    /// Returns NotFound for every GetBranchCommitSummaryAsync call — simulates a deleted branch.
    /// </summary>
    private sealed class NotFoundCommitProviderQueries : IPostExitProviderQueries
    {
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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<BranchCommitSummary>.Fail(
                    new Error("Provider.NotFound", "Branch not found") { Kind = ErrorKind.NotFound }));
    }

    /// <summary>
    /// Returns <paramref name="commit"/> on the first call, then a generic (non-NotFound) failure
    /// on all subsequent calls — simulates a transient provider outage after an initial success.
    /// </summary>
    private sealed class ScriptedThenFailingCommitProviderQueries(BranchCommitSummary commit)
        : IPostExitProviderQueries
    {
        private int _callCount;

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

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            if (_callCount == 0)
            {
                _callCount++;
                return Task.FromResult(Result<BranchCommitSummary>.Ok(commit));
            }

            _callCount++;
            return Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.Unavailable", "provider down")));
        }
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> that advances by one second on each call to
    /// <see cref="GetUtcNow"/> — guarantees distinct, strictly-increasing timestamps across ticks.
    /// </summary>
    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _current = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            DateTimeOffset value = _current;
            _current = _current.AddSeconds(1);
            return value;
        }
    }

    /// <summary>
    /// Orchestrator that always returns the given <see cref="WorkerStatus"/> — useful for tests
    /// that need a stable running container across many ticks without scripted log behaviour.
    /// </summary>
    private sealed class ConstantStatusOrchestrator(WorkerStatus status) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "no dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(status));

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

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
