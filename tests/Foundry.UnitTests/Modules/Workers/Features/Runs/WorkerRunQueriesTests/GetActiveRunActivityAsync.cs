using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetActiveRunActivityAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetActiveRunActivityAsync()
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

    private async Task<ActiveRun> SeedActiveRunAsync(IssueId? issueId = null, int commitCount = 0)
    {
        IssueId id = issueId ?? IssueId.New();
        StartingRun starting = StartingRun.Begin(id, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active"),
            BranchName.From("feat/1-active"),
            MonitoredRepositoryId.New());

        if (commitCount > 0)
        {
            active.RecordBranchCommitCount(commitCount, "abc123", DateTimeOffset.UtcNow);
        }

        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return active;
    }

    private async Task SeedCompletedRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-completed"),
            BranchName.From("feat/2-completed"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/2-completed"),
            pullRequestUrl: null);

        _dbContext.Set<WorkerRun>().Add(completed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task SeedFailedRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed"),
            BranchName.From("feat/3-failed"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null);

        _dbContext.Set<WorkerRun>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task WhenNoRunsExist_ReturnsEmptyCollection()
    {
        // Arrange
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenActiveRunExists_ReturnsOneActivity()
    {
        // Arrange
        ActiveRun run = await SeedActiveRunAsync();
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenActiveRunExists_MapsWorkerRunIdAndIssueId()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun run = await SeedActiveRunAsync(issueId: issueId);
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        WorkerActivity activity = result.ShouldHaveSingleItem();
        activity.ShouldSatisfyAllConditions(
            () => activity.WorkerRunId.ShouldBe(run.Id.Value),
            () => activity.IssueId.ShouldBe(issueId.Value));
    }

    [Fact]
    public async Task WhenActiveRunHasCommits_MapsCommitCount()
    {
        // Arrange
        ActiveRun run = await SeedActiveRunAsync(commitCount: 3);
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        WorkerActivity activity = result.ShouldHaveSingleItem();
        activity.CommitCount.ShouldBe(3);
    }

    [Fact]
    public async Task WhenCompletedAndFailedRunsExist_ExcludesNonActiveRuns()
    {
        // Arrange
        await SeedCompletedRunAsync();
        await SeedFailedRunAsync();
        ActiveRun activeRun = await SeedActiveRunAsync();
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContain(a => a.WorkerRunId == activeRun.Id.Value);
    }

    [Fact]
    public async Task WhenMultipleActiveRunsExist_ReturnsAllActiveRuns()
    {
        // Arrange
        ActiveRun run1 = await SeedActiveRunAsync();
        ActiveRun run2 = await SeedActiveRunAsync();
        WorkerRunQueries sut = new(_dbContext);

        // Act
        IReadOnlyCollection<WorkerActivity> result = await sut.GetActiveRunActivityAsync(
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(a => a.WorkerRunId == run1.Id.Value);
        result.ShouldContain(a => a.WorkerRunId == run2.Id.Value);
    }
}
