using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerRunQueriesTests;

public sealed class GetRunTotalsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    private static readonly DateTimeOffset WindowStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OutOfWindow = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public GetRunTotalsAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<CompletedRun> SeedCompletedRunAsync(
        DateTimeOffset? createdAt = null,
        RunResultSummary? resultSummary = null)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-completed"),
            BranchName.From("feat/1-completed"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/1-completed"),
            pullRequestUrl: null,
            resultSummary: resultSummary);

        _dbContext.Set<WorkerRun>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (createdAt.HasValue)
        {
            // CreatedAt is set by the domain factory using DateTimeOffset.UtcNow — override via
            // EF entry to seed runs with past timestamps for date-window filtering tests.
            _dbContext.Entry(completed).Property(r => r.CreatedAt).CurrentValue = createdAt.Value;
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
        return completed;
    }

    private async Task<FailedRun> SeedFailedRunAsync(
        DateTimeOffset? createdAt = null,
        RunResultSummary? resultSummary = null)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed"),
            BranchName.From("feat/2-failed"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            resultSummary: resultSummary);

        _dbContext.Set<WorkerRun>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (createdAt.HasValue)
        {
            // Override CreatedAt for date-window filtering tests.
            _dbContext.Entry(failed).Property(r => r.CreatedAt).CurrentValue = createdAt.Value;
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
        return failed;
    }

    private async Task<ActiveRun> SeedActiveRunAsync(DateTimeOffset? createdAt = null)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active"),
            BranchName.From("feat/3-active"),
            MonitoredRepositoryId.New());

        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (createdAt.HasValue)
        {
            // Override CreatedAt for date-window filtering tests.
            _dbContext.Entry(active).Property(r => r.CreatedAt).CurrentValue = createdAt.Value;
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
        return active;
    }

    private async Task<StartingRun> SeedStartingRunAsync(DateTimeOffset? createdAt = null)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());

        _dbContext.Set<WorkerRun>().Add(starting);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (createdAt.HasValue)
        {
            // Override CreatedAt for date-window filtering tests.
            _dbContext.Entry(starting).Property(r => r.CreatedAt).CurrentValue = createdAt.Value;
            await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _dbContext.ChangeTracker.Clear();
        return starting;
    }

    [Fact]
    public async Task WhenNoRunsInWindow_ReturnsAllZeros()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(0),
            () => result.DurationMs.ShouldBe(0L),
            () => result.NumTurns.ShouldBe(0),
            () => result.TotalCostUsd.ShouldBe(0m),
            () => result.InputTokens.ShouldBe(0L),
            () => result.OutputTokens.ShouldBe(0L));
    }

    [Fact]
    public async Task WhenCompletedAndFailedRunsExist_SumsAllFields()
    {
        // Arrange
        RunResultSummary completedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 3000L,
            numTurns: 4,
            totalCostUsd: 0.10m,
            inputTokens: 500,
            outputTokens: 800);

        RunResultSummary failedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: true,
            durationMs: 2000L,
            numTurns: 2,
            totalCostUsd: 0.05m,
            inputTokens: 300,
            outputTokens: 400);

        await SeedCompletedRunAsync(createdAt: InWindow, resultSummary: completedSummary);
        await SeedFailedRunAsync(createdAt: InWindow, resultSummary: failedSummary);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(2),
            () => result.DurationMs.ShouldBe(5000L),
            () => result.NumTurns.ShouldBe(6),
            () => result.TotalCostUsd.ShouldBe(0.15m),
            () => result.InputTokens.ShouldBe(800L),
            () => result.OutputTokens.ShouldBe(1200L));
    }

    [Fact]
    public async Task WhenActiveRunInWindow_AddsToRunCountOnly()
    {
        // Arrange
        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 2,
            totalCostUsd: 0.02m,
            inputTokens: 100,
            outputTokens: 200);

        await SeedCompletedRunAsync(createdAt: InWindow, resultSummary: summary);
        await SeedActiveRunAsync(createdAt: InWindow);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(2),
            () => result.DurationMs.ShouldBe(1000L),
            () => result.NumTurns.ShouldBe(2),
            () => result.TotalCostUsd.ShouldBe(0.02m),
            () => result.InputTokens.ShouldBe(100L),
            () => result.OutputTokens.ShouldBe(200L));
    }

    [Fact]
    public async Task WhenFromIsInclusive_IncludesRunAtFromBoundary()
    {
        // Arrange
        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 1,
            totalCostUsd: 0.01m,
            inputTokens: 50,
            outputTokens: 100);

        // Seed a run exactly at the from boundary (inclusive)
        await SeedCompletedRunAsync(createdAt: WindowStart, resultSummary: summary);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.RunCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenToIsExclusive_ExcludesRunAtToBoundary()
    {
        // Arrange
        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 1,
            totalCostUsd: 0.01m,
            inputTokens: 50,
            outputTokens: 100);

        // Seed a run exactly at the to boundary (exclusive — should not be included)
        await SeedCompletedRunAsync(createdAt: WindowEnd, resultSummary: summary);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.RunCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenRunsOutsideWindow_ExcludesThoseRuns()
    {
        // Arrange
        RunResultSummary inSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 2000L,
            numTurns: 3,
            totalCostUsd: 0.03m,
            inputTokens: 200,
            outputTokens: 300);

        RunResultSummary outSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 9999L,
            numTurns: 99,
            totalCostUsd: 9.99m,
            inputTokens: 9999,
            outputTokens: 9999);

        await SeedCompletedRunAsync(createdAt: InWindow, resultSummary: inSummary);
        await SeedCompletedRunAsync(createdAt: OutOfWindow, resultSummary: outSummary);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(1),
            () => result.DurationMs.ShouldBe(2000L),
            () => result.NumTurns.ShouldBe(3),
            () => result.TotalCostUsd.ShouldBe(0.03m),
            () => result.InputTokens.ShouldBe(200L),
            () => result.OutputTokens.ShouldBe(300L));
    }

    [Fact]
    public async Task WhenBothCompletedAndFailedExist_BothCountedInRunCount()
    {
        // Arrange
        await SeedCompletedRunAsync(createdAt: InWindow);
        await SeedFailedRunAsync(createdAt: InWindow);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert
        result.RunCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenRunsHaveNoResultSummary_TelemetryFieldsAreZero()
    {
        // Arrange
        // Seed completed and failed runs in-window with no ResultSummary (null telemetry).
        await SeedCompletedRunAsync(createdAt: InWindow, resultSummary: null);
        await SeedFailedRunAsync(createdAt: InWindow, resultSummary: null);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert — one logical outcome: all telemetry fields zero, run count still counts the rows.
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(2),
            () => result.DurationMs.ShouldBe(0L),
            () => result.NumTurns.ShouldBe(0),
            () => result.TotalCostUsd.ShouldBe(0m),
            () => result.InputTokens.ShouldBe(0L),
            () => result.OutputTokens.ShouldBe(0L));
    }

    [Fact]
    public async Task WhenStartingRunInWindow_AddsToRunCountOnly()
    {
        // Arrange
        RunResultSummary completedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 2,
            totalCostUsd: 0.02m,
            inputTokens: 100,
            outputTokens: 200);

        await SeedCompletedRunAsync(createdAt: InWindow, resultSummary: completedSummary);
        await SeedStartingRunAsync(createdAt: InWindow);

        WorkerRunQueries sut = new(_dbContext);

        // Act
        RunTotals result = await sut.GetRunTotalsAsync(
            WindowStart,
            WindowEnd,
            TestContext.Current.CancellationToken);

        // Assert — StartingRun adds to RunCount but contributes no telemetry sums.
        result.ShouldSatisfyAllConditions(
            () => result.RunCount.ShouldBe(2),
            () => result.DurationMs.ShouldBe(1000L),
            () => result.NumTurns.ShouldBe(2),
            () => result.TotalCostUsd.ShouldBe(0.02m),
            () => result.InputTokens.ShouldBe(100L),
            () => result.OutputTokens.ShouldBe(200L));
    }
}
