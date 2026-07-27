using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistCompletedRunSummary : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistCompletedRunSummary()
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
    public async Task WhenCompletedRunHasSummary_RoundTripsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-summary"),
            BranchName.From("feat/42-summary-test"),
            MonitoredRepositoryId.New());

        RunResultSummary summary = RunResultSummary.Create(
            resultText: "success",
            subtype: "final",
            isError: false,
            durationMs: 55000L,
            numTurns: 7,
            totalCostUsd: 0.12m,
            inputTokens: 3000,
            outputTokens: 1500);

        CompletedRun run = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/42-summary-test"),
            pullRequestUrl: null,
            resultSummary: summary);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedRun reloaded = result.ShouldBeOfType<CompletedRun>();
        RunResultSummary reloadedSummary = reloaded.ResultSummary.ShouldNotBeNull();
        reloadedSummary.ShouldSatisfyAllConditions(
            () => reloadedSummary.ResultText.ShouldBe("success"),
            () => reloadedSummary.Subtype.ShouldBe("final"),
            () => reloadedSummary.IsError.ShouldBeFalse(),
            () => reloadedSummary.DurationMs.ShouldBe(55000L),
            () => reloadedSummary.NumTurns.ShouldBe(7),
            () => reloadedSummary.TotalCostUsd.ShouldBe(0.12m),
            () => reloadedSummary.InputTokens.ShouldBe(3000),
            () => reloadedSummary.OutputTokens.ShouldBe(1500));
    }

    [Fact]
    public async Task WhenCompletedRunHasNullSummary_ReloadsWithNullSummary()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-no-summary"),
            BranchName.From("feat/1-no-summary"),
            MonitoredRepositoryId.New());

        CompletedRun run = active.Complete(exitCode: 0, branchName: null, pullRequestUrl: null, resultSummary: null);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedRun reloaded = result.ShouldBeOfType<CompletedRun>();
        reloaded.ResultSummary.ShouldBeNull();
    }
}
