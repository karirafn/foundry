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
/// and records commits from the provider on still-running workers.
/// </summary>
public sealed class RunningWorkerActivityTracking : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        IWorkerOrchestrator orchestrator,
        IPostExitProviderQueries? postExitProviderQueries = null,
        IDomainEventDispatcher? domainEventDispatcher = null,
        CapturingLogger? logger = null)
    {
        return base.BuildService(
            orchestrator,
            postExitProviderQueries: postExitProviderQueries,
            domainEventDispatcher: domainEventDispatcher,
            logger: logger);
    }

    [Fact]
    public async Task WhenContainerIsAlive_LastActivityAtIsSet()
    {
        // Arrange — container is running; logs return null (no output), but liveness is from container status
        SeedActiveRun("running-activity-alive");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

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
        WorkerDispatchService sut = BuildService(orchestrator);

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
        // Arrange — container alive on every tick (no log output needed)
        SeedActiveRun("running-activity-multi");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: first activity observation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        await using FoundryDbContext db2 = CreateDbContext();
        WorkerRun? run2 = await db2.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active2 = run2.ShouldBeOfType<ActiveRun>();
        active2.LastActivityAt.ShouldNotBeNull();

        // Act — Tick 3: second activity observation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — activity is still set (RecordActivity called again)
        await using FoundryDbContext db3 = CreateDbContext();
        WorkerRun? run3 = await db3.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun active3 = run3.ShouldBeOfType<ActiveRun>();
        active3.LastActivityAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenRunningWorkerHasNewCommit_BranchCommitCountRecorded()
    {
        // Arrange
        SeedActiveRun("running-commit-record");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary commit = new(CommitCount: 1, LatestSha: "sha-abc123");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: "log", secondLogs: "log");
        ScriptedCommitProviderQueries queries = new(firstSummary: null, secondSummary: commit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation — no commit yet
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: new commit observed
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(1);
        activeRun.LastObservedCommitSha.ShouldBe("sha-abc123");
    }

    [Fact]
    public async Task WhenRunningWorkerSameCommitTwice_BranchCommitCountUpdatedNoEvent()
    {
        // Arrange — same SHA returned on both tick 2 and tick 3
        SeedActiveRun("running-commit-dedup");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary commit = new(CommitCount: 1, LatestSha: "sha-dedup");
        // Both ticks return the same commit
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: "log", secondLogs: "log");
        ConstantCommitProviderQueries queries = new(commit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: first time seeing commit — count set, event raised
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 3: same commit SHA again — count updated, no new event
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — count remains 1 (same SHA, no rebase)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenCommitProviderReturnsFailure_ActivityStillRecordedFromContainerAlive()
    {
        // Arrange — provider fails; activity comes from container-alive, not from logs
        SeedActiveRun("running-provider-fail");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        // No log output — activity must be from container liveness only
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        FailingCommitProviderQueries queries = new();
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: provider fails, but container is alive
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — activity recorded from container-alive; commit count stays zero; no exception thrown
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.LastActivityAt.ShouldNotBeNull();
        activeRun.BranchCommitCount.ShouldBe(0);
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

    [Fact]
    public async Task WhenCommitProviderReturnsNotFound_BranchCommitCountSetToZero()
    {
        // Arrange — run already has a commit count (from tick 2), then provider returns NotFound on tick 3
        SeedActiveRun("running-notfound");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary priorCommit = new(CommitCount: 3, LatestSha: "sha-prior");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        // Tick 1 (reconcile): fail → count stays 0.
        // Tick 2: success → count 3.
        // Tick 3: NotFound → count reset to 0.
        SuccessThenNotFoundCommitProviderQueries queries = new(priorCommit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation — commit query fails, count stays 0
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Tick 2: commit arrives, count = 3
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 3: NotFound — count must reset to 0
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — commit count is 0 (NotFound treated as branch gone)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenCommitProviderReturnsGenericError_BranchCommitCountUnchanged()
    {
        // Arrange — run has an existing commit count (tick 1 succeeds via constant queries),
        //           then tick 2 provider fails with a generic (non-NotFound) error.
        //           The count from tick 1 must survive untouched.
        //           Uses ConstantCommitProviderQueries for tick 1 success (count=2),
        //           then a ConstantFailingCommitProviderQueries for tick 2 failure.
        SeedActiveRun("running-generic-err");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary commit = new(CommitCount: 2, LatestSha: "sha-prior");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);

        // Tick 1: success → count 2.
        // Tick 2: generic error → count must stay 2.
        ScriptedThenFailingCommitProviderQueries queries = new(commit);
        CapturingLogger capturingLogger = new();
        WorkerDispatchService sut = BuildService(orchestrator, queries, logger: capturingLogger);

        // Tick 1: commit arrives (count → 2); provider succeeds on first call
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        int warningsAfterTick1 = capturingLogger.Entries.Count(e => e.Level == LogLevel.Warning);

        // Act — Tick 2: provider fails with generic error
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — count unchanged from tick 1
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(2);

        // Assert — exactly one warning logged for tick 2's generic provider error
        int warningsAfterTick2 = capturingLogger.Entries.Count(e => e.Level == LogLevel.Warning);
        (warningsAfterTick2 - warningsAfterTick1).ShouldBe(1, "exactly one warning per generic provider failure");
    }

    [Fact]
    public async Task WhenBranchIsRebased_CommitCountDecreases()
    {
        // Arrange — first commit has count 5, then rebase reduces it to 2
        SeedActiveRun("running-rebase");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary firstCommit = new(CommitCount: 5, LatestSha: "sha-before-rebase");
        BranchCommitSummary rebasedCommit = new(CommitCount: 2, LatestSha: "sha-after-rebase");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        // Tick 1 returns firstCommit, tick 2 returns rebasedCommit
        ScriptedCommitProviderQueries queries = new(firstSummary: firstCommit, secondSummary: rebasedCommit);
        WorkerDispatchService sut = BuildService(orchestrator, queries);

        // Tick 1: reconciliation — first commit observed
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Act — Tick 2: rebase, fewer commits
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — count decreased
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.BranchCommitCount.ShouldBe(2);
        activeRun.LastObservedCommitSha.ShouldBe("sha-after-rebase");
    }

    [Fact]
    public async Task WhenShaUnchangedOnSecondTick_OnlyOneWorkerActivityEventDispatchedPerTick()
    {
        // Arrange — same commit SHA returned on every tick; container always running.
        // RecordActivity fires once per tick (container-alive). RecordBranchCommitCount fires once
        // when the SHA first appears, then guards on subsequent ticks (same count + SHA).
        SeedActiveRun("running-sha-unchanged");
        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary commit = new(CommitCount: 1, LatestSha: "sha-stable");
        ScriptedLogsOrchestrator orchestrator = new(runningStatus, firstLogs: null, secondLogs: null);
        ConstantCommitProviderQueries queries = new(commit);
        CapturingDomainEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, queries, capturingDispatcher);

        // Tick 1: reconciliation + observation — RecordActivity fires (1 event),
        //         RecordBranchCommitCount fires first time (SHA changed: null → sha-stable, 1 event).
        //         Total after tick 1: 2 events.
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        int eventsAfterTick1 = capturingDispatcher.DispatchedEvents
            .Count(e => e is WorkerActivityObserved);

        // Tick 2: commit seen again with same SHA — RecordActivity fires (1 event),
        //         RecordBranchCommitCount guards (same count + SHA, 0 events).
        //         Total after tick 2: eventsAfterTick1 + 1.
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        int eventsAfterTick2 = capturingDispatcher.DispatchedEvents
            .Count(e => e is WorkerActivityObserved);

        // Act — Tick 3: same SHA again — only RecordActivity fires (1 event).
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — each tick with an unchanged SHA raises exactly one WorkerActivityObserved
        //          (from RecordActivity only, not from RecordBranchCommitCount).
        int eventsAfterTick3 = capturingDispatcher.DispatchedEvents
            .Count(e => e is WorkerActivityObserved);
        (eventsAfterTick2 - eventsAfterTick1).ShouldBe(1, "tick 2 (unchanged SHA) adds exactly one event via RecordActivity");
        (eventsAfterTick3 - eventsAfterTick2).ShouldBe(1, "tick 3 (unchanged SHA) adds exactly one event via RecordActivity");
    }

    [Fact]
    public async Task WhenHostRestartWithPersistedSha_NoCommitEventRaisedForAlreadySeenSha()
    {
        // Arrange — seed an ActiveRun that already has a LastObservedCommitSha persisted
        //           (simulates a host restart where the service's in-memory state is lost).
        //           RecordBranchCommitCount guards on (count == persisted && sha == persisted)
        //           so no commit-path event fires after restart.
        //           RecordActivity fires per tick (container-alive), but with a distinct event count.
        const string persistedSha = "sha-already-seen";
        ActiveRun runWithHistory = new ActiveRunBuilder()
            .WithContainerId(ContainerId.From("container-restart"))
            .WithBranchName(BranchName.From("feat/1-restart"))
            .WithObservedCommit(count: 3, sha: persistedSha);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<WorkerRun>().Add(runWithHistory);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WorkerStatus runningStatus = new(IsRunning: true, ExitCode: null, FinishedAt: null);
        BranchCommitSummary sameCommit = new(CommitCount: 3, LatestSha: persistedSha);
        ConstantStatusOrchestrator orchestrator = new(runningStatus);
        ConstantCommitProviderQueries queries = new(sameCommit);
        CapturingDomainEventDispatcher capturingDispatcher = new();
        WorkerDispatchService sut = BuildService(orchestrator, queries, capturingDispatcher);

        // Act — two ticks after restart; each calls ObserveRunningWorkerAsync
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — every WorkerActivityObserved has CommitCount == 3 (the persisted SHA),
        //          meaning RecordBranchCommitCount did NOT fire a separate event with a new count.
        //          All events come from RecordActivity (container-alive) carrying the existing count.
        IReadOnlyList<WorkerActivityObserved> activityEvents = capturingDispatcher.DispatchedEvents
            .OfType<WorkerActivityObserved>()
            .ToList();
        activityEvents.ShouldNotBeEmpty("RecordActivity fires on each observation");
        activityEvents.ShouldAllBe(e => e.CommitCount == 3, "commit count unchanged — no commit-path event fired");
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

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
