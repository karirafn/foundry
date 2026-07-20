using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistFailedRunSummary : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistFailedRunSummary()
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

    [Fact]
    public async Task WhenFailedRunHasSummary_RoundTripsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed-summary"),
            BranchName.From("feat/99-failed"),
            MonitoredRepositoryId.New());

        RunResultSummary summary = RunResultSummary.Create(
            resultText: "error_during_turn",
            subtype: "error",
            isError: true,
            durationMs: 8000L,
            numTurns: 2,
            totalCostUsd: null,
            inputTokens: null,
            outputTokens: null);

        FailedRun run = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: "some output",
            resultSummary: summary);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        RunResultSummary reloadedSummary = reloaded.ResultSummary.ShouldNotBeNull();
        reloadedSummary.ShouldSatisfyAllConditions(
            () => reloadedSummary.ResultText.ShouldBe("error_during_turn"),
            () => reloadedSummary.Subtype.ShouldBe("error"),
            () => reloadedSummary.IsError.ShouldBeTrue(),
            () => reloadedSummary.DurationMs.ShouldBe(8000L),
            () => reloadedSummary.NumTurns.ShouldBe(2),
            () => reloadedSummary.TotalCostUsd.ShouldBeNull(),
            () => reloadedSummary.InputTokens.ShouldBeNull(),
            () => reloadedSummary.OutputTokens.ShouldBeNull());
    }

    [Fact]
    public async Task WhenFailedRunHasNullSummary_ReloadsWithNullSummary()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun run = starting.Fail(new FailureReason.TimedOut());

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        FailedRun reloaded = result.ShouldBeOfType<FailedRun>();
        reloaded.ResultSummary.ShouldBeNull();
    }
}
