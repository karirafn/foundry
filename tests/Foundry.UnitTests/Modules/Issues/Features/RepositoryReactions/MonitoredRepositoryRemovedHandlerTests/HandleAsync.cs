using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.RepositoryReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.RepositoryReactions.MonitoredRepositoryRemovedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly MonitoredRepositoryRemovedHandler _sut;
    private readonly MonitoredRepositoryId _removedRepositoryId = MonitoredRepositoryId.New();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new MonitoredRepositoryRemovedHandler(
            _dbContext,
            new RepositoryRemovalBlockers(_dbContext, new Foundry.Modules.Workers.Features.Runs.WorkerRunQueries(_dbContext)),
            NullLogger<MonitoredRepositoryRemovedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DeletesEveryRestingIssueAndKeepsOnlyTheCompletedOne()
    {
        // Arrange
        await SeedAsync(Builder(1).FreshQueued());
        await SeedAsync(Builder(2).TriageBlocked([99]));
        await SeedAsync(Builder(3).WithFailedAt(Now).Failed());
        await SeedAsync(Builder(4).Review());
        await SeedAsync(Builder(5).WithFailedAt(Now).PublishFailed());
        await SeedAsync(Builder(6).WithFailedAt(Now).PublishBlocked());
        await SeedAsync(Builder(7).WithCompletedAt(Now).Completed());

        // Act
        await DeliverAsync();

        // Assert
        (await IssueNumbersAsync()).ShouldBe([7]);
    }

    [Fact]
    public async Task KeepsEveryIssueWhoseWorkerIsStillRunning()
    {
        // Arrange
        await SeedAsync(Builder(1).TriageInProgress());
        await SeedAsync(Builder(2).InProgress());
        await SeedAsync(Builder(3).RevisionInProgress());
        await SeedAsync(Builder(4).ReviewInProgress());

        // Act
        await DeliverAsync();

        // Assert
        (await IssueNumbersAsync()).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task DeletesAnInFlightIssueWhoseRunWaitsForAPublishRetry()
    {
        // Arrange
        InProgressIssue retrying = Builder(1).InProgress();
        await SeedAsync(retrying);
        await SeedRunAsync(StartingRun.Begin(retrying.Id, WorkerRunId.New())
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/1-run"), _removedRepositoryId)
            .BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, Now),
                containerOutput: null,
                verdict: null,
                pullRequestText: null)
            .FailPublish("provider down", transient: true, Now));
        await SeedAsync(Builder(2).InProgress());

        // Act
        await DeliverAsync();

        // Assert
        (await IssueNumbersAsync()).ShouldBe([2]);
    }

    [Fact]
    public async Task LeavesTheIssuesOfOtherRepositories()
    {
        // Arrange
        await SeedAsync(new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(1)
            .WithDetectedAt(Now)
            .FreshQueued());

        // Act
        await DeliverAsync();

        // Assert
        (await IssueNumbersAsync()).ShouldBe([1]);
    }

    [Fact]
    public async Task WhenRedelivered_FindsNothingToDelete()
    {
        // Arrange
        await SeedAsync(Builder(1).FreshQueued());
        await SeedAsync(Builder(2).WithCompletedAt(Now).Completed());
        await DeliverAsync();

        // Act
        await DeliverAsync();

        // Assert
        (await IssueNumbersAsync()).ShouldBe([2]);
    }

    private IssueBuilder Builder(int issueNumber) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_removedRepositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFeedbackCutoffAt(Now);

    private async Task SeedAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task SeedRunAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task DeliverAsync()
    {
        await _sut.HandleAsync(
            new MonitoredRepositoryRemoved(_removedRepositoryId, Now),
            TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<List<int>> IssueNumbersAsync() =>
        await _dbContext.Set<Issue>()
            .OrderBy(i => i.IssueNumber)
            .Select(i => i.IssueNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
}
