using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.TransientRetry;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.TransientRetry;

public sealed class TickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _dispatcher;

    public TickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _dispatcher = new CapturingDomainEventDispatcher();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private FailedIssue SeedTransientFailedIssue(DateTimeOffset failedAt, int issueNumber = 1)
    {
        FailedIssue failed = new IssueBuilder()
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .WithFailureCategory(FailureCategory.TransientApiError)
            .WithFailedAt(failedAt)
            .Failed();
        _dbContext.Set<Issue>().Add(failed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private ContinuableFailedIssue SeedTransientContinuableFailedIssue(DateTimeOffset failedAt)
    {
        ContinuableFailedIssue continuableFailed = new IssueBuilder()
            .WithIssueNumber(10)
            .WithTitle("Test Issue With Branch")
            .WithDetectedAt(DateTimeOffset.UtcNow.AddHours(-2))
            .WithBranchName("feat/10-fix")
            .WithFailureCategory(FailureCategory.TransientApiError)
            .WithFailedAt(failedAt)
            .ContinuableFailed();
        _dbContext.Set<Issue>().Add(continuableFailed);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return continuableFailed;
    }

    private TransientRetryService BuildSut(
        IWorkerRunQueries? workerRunQueries = null,
        DateTimeOffset? now = null)
    {
        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(opts =>
            opts.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IDomainEventDispatcher>(_ => _dispatcher);
        services.AddScoped<IWorkerRunQueries>(_ => workerRunQueries ?? new NullWorkerRunQueries());
        ServiceProvider sp = services.BuildServiceProvider();

        DateTimeOffset resolvedNow = now ?? DateTimeOffset.UtcNow;

        return new TransientRetryService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TransientRetryService>.Instance,
            resolvedNow);
    }

    [Fact]
    public async Task WhenTransientFailedIssueIsDueAndUnderCap_TransitionsToQueued()
    {
        // Arrange — failed 2 minutes ago (backoff 1 minute), 1 prior transient run
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddMinutes(-2);
        FailedIssue failed = SeedTransientFailedIssue(failedAt);

        // Stub: 1 prior transient run (under cap of 2)
        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTransientTriageFailureIsDueAndUnderCap_RetriesTriage()
    {
        // Arrange — failed 2 minutes ago (backoff 1 minute), 1 prior transient run
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TriageFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(20)
            .WithFailureCategory(FailureCategory.TransientApiError)
            .WithFailedAt(now.AddMinutes(-2))
            .TriageFailed();
        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        TransientRetryService sut = BuildSut(workerRunQueries: new StubWorkerRunQueries(count: 1), now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<TriageQueuedIssue>();
    }

    [Fact]
    public async Task WhenATriageFailureIsNotTransient_ItStaysParked()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TriageFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(21)
            .WithFailureCategory(FailureCategory.VerdictInvalid)
            .WithFailedAt(now.AddHours(-2))
            .TriageFailed();
        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        TransientRetryService sut = BuildSut(workerRunQueries: new StubWorkerRunQueries(count: 0), now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<TriageFailedIssue>();
    }

    public static TheoryData<FailureCategory> BlockerFailureCategories =>
        [FailureCategory.BlockerCycle, FailureCategory.BlockerLinkFailed];

    [Theory]
    [MemberData(nameof(BlockerFailureCategories))]
    public async Task WhenATriageFailureIsABlockerFailure_ItIsNeverAutoRetried(FailureCategory category)
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TriageFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(22)
            .WithFailureCategory(category)
            .WithFailedAt(now.AddHours(-2))
            .TriageFailed();
        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        TransientRetryService sut = BuildSut(workerRunQueries: new StubWorkerRunQueries(count: 0), now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<TriageFailedIssue>();
    }

    [Fact]
    public async Task WhenTransientFailedIssueIsAtCap_StaysInFailedState()
    {
        // Arrange — failed 2 minutes ago (backoff 1 minute), 2 prior transient runs (at cap)
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddMinutes(-2);
        FailedIssue failed = SeedTransientFailedIssue(failedAt);

        // Stub: 2 prior transient runs (= MaxTransientRetries → at cap, do not retry)
        StubWorkerRunQueries stub = new(count: 2);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — stays failed
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenTransientFailedIssueIsNotYetDue_StaysInFailedState()
    {
        // Arrange — failed 30 seconds ago (backoff 1 minute — not elapsed), 1 prior transient run
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddSeconds(-30);
        FailedIssue failed = SeedTransientFailedIssue(failedAt);

        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — stays failed
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenRestartMidBackoff_StillTransitionsAfterBackoffElapsed()
    {
        // Arrange — simulates a restart mid-backoff: FailedAt is persisted in the past
        // and now is set to after the full backoff has elapsed. The service recomputes
        // due-ness from the persisted FailedAt, so it fires even across host restarts (AC 6).
        DateTimeOffset failedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = failedAt.AddMinutes(5); // well past the 1-minute backoff
        FailedIssue failed = SeedTransientFailedIssue(failedAt);

        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — due-ness was recomputed from persisted FailedAt; fires correctly
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTransientContinuableFailedIssueIsDue_TransitionsToContinuationQueued()
    {
        // Arrange — ContinuableFailedIssue with transient_api_error, 1 prior transient run, elapsed backoff
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddMinutes(-2);
        ContinuableFailedIssue continuableFailed = SeedTransientContinuableFailedIssue(failedAt);

        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — ContinuableFailedIssue.Retry() → ContinuationQueuedIssue (AC 4 branch)
        _dbContext.ChangeTracker.Clear();
        Issue? persisted = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == continuableFailed.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<ContinuationQueuedIssue>();
    }

    [Fact]
    public async Task WhenTransientFailedIssueIsDue_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddMinutes(-2);
        FailedIssue failed = SeedTransientFailedIssue(failedAt);

        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — IssueQueued domain event raised
        _dispatcher.DispatchedEvents
            .OfType<IssueQueued>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenMultipleIssueDue_AllTransitioned()
    {
        // Arrange — two transient failed issues, both due
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset failedAt = now.AddMinutes(-5);
        FailedIssue failed1 = SeedTransientFailedIssue(failedAt, issueNumber: 1);
        FailedIssue failed2 = SeedTransientFailedIssue(failedAt, issueNumber: 2);

        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — both transitioned
        _dbContext.ChangeTracker.Clear();
        Issue? persisted1 = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed1.Id, TestContext.Current.CancellationToken);
        Issue? persisted2 = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == failed2.Id, TestContext.Current.CancellationToken);
        persisted1.ShouldBeOfType<FreshQueuedIssue>();
        persisted2.ShouldBeOfType<FreshQueuedIssue>();
    }

    private async Task<Issue?> TickAndReloadAsync(Issue seeded, int streak, DateTimeOffset now)
    {
        _dbContext.Set<Issue>().Add(seeded);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        TransientRetryService sut = BuildSut(workerRunQueries: new StubWorkerRunQueries(count: streak), now: now);

        await sut.TickForTest(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == seeded.Id, TestContext.Current.CancellationToken);
    }

    private static FailedIssue FailedWith(FailureCategory category, DateTimeOffset failedAt) =>
        new IssueBuilder()
            .WithIssueNumber(30)
            .WithFailureCategory(category)
            .WithFailedAt(failedAt)
            .Failed();

    [Fact]
    public async Task WhenTransientRevisionFailureIsDueAndUnderCap_RetriesTheRevision()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RevisionFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(31)
            .WithFailureCategory(FailureCategory.TransientApiError)
            .WithFailedAt(now.AddMinutes(-2))
            .RevisionFailed();

        // Act
        Issue? persisted = await TickAndReloadAsync(failed, streak: 1, now);

        // Assert
        persisted.ShouldBeOfType<RevisionQueuedIssue>();
    }

    [Fact]
    public async Task WhenTransientRevisionFailureIsDue_RaisesIssueRevisionQueuedDomainEvent()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RevisionFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(32)
            .WithFailureCategory(FailureCategory.ProviderUnreachable)
            .WithFailedAt(now.AddMinutes(-3))
            .RevisionFailed();

        // Act
        await TickAndReloadAsync(failed, streak: 1, now);

        // Assert
        _dispatcher.DispatchedEvents.OfType<IssueRevisionQueued>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenARevisionFailureIsNotAutoRetryable_ItStaysParked()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RevisionFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(33)
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .WithFailedAt(now.AddHours(-2))
            .RevisionFailed();

        // Act
        Issue? persisted = await TickAndReloadAsync(failed, streak: 0, now);

        // Assert
        persisted.ShouldBeOfType<RevisionFailedIssue>();
    }

    [Fact]
    public async Task WhenProviderUnreachableFailedOneMinuteAgo_ItWaitsForItsTwoMinuteBackoff()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddMinutes(-1.5)), streak: 1, now);

        // Assert
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenProviderUnreachableBackoffHasElapsed_TransitionsToQueued()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddMinutes(-2.5)), streak: 1, now);

        // Assert
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenProviderUnreachableStreakIsFour_ItWaitsSixteenMinutes()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddMinutes(-15)), streak: 4, now);

        // Assert
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenProviderUnreachableStreakIsFiveTimes_StaysFailed()
    {
        // Arrange — U×5: streak 5 ≥ 5
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddHours(-2)), streak: 5, now);

        // Assert
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenLatestFailureIsTransientApiErrorAfterAnUnreachable_ItsMaximumOfTwoStopsTheStreak()
    {
        // Arrange — U×1 then E: streak 2 ≥ 2
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.TransientApiError, now.AddHours(-2)), streak: 2, now);

        // Assert
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenLatestFailureIsUnreachableAfterATransientApiError_ItRetriesUnderItsMaximumOfFive()
    {
        // Arrange — E×1 then U: streak 2 < 5, backoff 4 minutes
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddMinutes(-5)), streak: 2, now);

        // Assert
        persisted.ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTwoTransientApiErrorsThenThreeUnreachables_StaysFailed()
    {
        // Arrange — E×2 then U×3: streak 5 ≥ 5
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Act
        Issue? persisted = await TickAndReloadAsync(
            FailedWith(FailureCategory.ProviderUnreachable, now.AddHours(-2)), streak: 5, now);

        // Assert
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenCountingTheStreak_PassesTheLatestCategorysMaximum()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _dbContext.Set<Issue>().Add(FailedWith(FailureCategory.ProviderUnreachable, now.AddMinutes(-5)));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        StubWorkerRunQueries stub = new(count: 1);
        TransientRetryService sut = BuildSut(workerRunQueries: stub, now: now);

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        stub.RequestedMaxAttempts.ShouldBe([5]);
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a fixed count of consecutive transient runs for all queries.
    /// </summary>
    private sealed class StubWorkerRunQueries(int count) : IWorkerRunQueries
    {
        public List<int> RequestedMaxAttempts { get; } = [];

        public Task<int> CountConsecutiveTransientRunsAsync(
            Guid issueId,
            int maxAttempts,
            CancellationToken cancellationToken)
        {
            RequestedMaxAttempts.Add(maxAttempts);
            return Task.FromResult(count);
        }

        public Task<Result<WorkerRunDetail>> GetWorkerRunDetailAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<WorkerRunDetail>.Fail(new Error("Test.NotFound", "Not found")));

        public Task<WorkerRunLogResult> GetWorkerRunLogAsync(
            Guid workerRunId,
            CancellationToken cancellationToken)
            => Task.FromResult<WorkerRunLogResult>(new WorkerRunLogResult.RunNotFound());

        public Task<IReadOnlyDictionary<Guid, RunAggregate>> GetRunAggregatesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, RunAggregate>>(new Dictionary<Guid, RunAggregate>());

        public Task<RunTotals> GetRunTotalsAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
            => Task.FromResult(new RunTotals(0, 0L, 0, 0m, TokenTotal.None, TokenTotal.None));

        public Task<IReadOnlyCollection<WorkerActivity>> GetActiveRunActivityAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<WorkerActivity>>([]);

        public Task<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>> GetPublishPhasesForIssuesAsync(
            IReadOnlyCollection<Guid> issueIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, WorkerRunPublishPhase>>(new Dictionary<Guid, WorkerRunPublishPhase>());
    }
}
