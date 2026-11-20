using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features.RepositoryReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.RepositoryReactions.RepositoryRemovalBlockersTests;

public sealed class GetBlockingIssuesAsync : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly RepositoryRemovalBlockers _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public GetBlockingIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new RepositoryRemovalBlockers(_dbContext, new WorkerRunQueries(_dbContext));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ReportsInFlightIssuesUnlessTheirRunWaitsForAPublishRetry()
    {
        // Arrange
        Issue active = await SeedAsync(Builder(1).InProgress());
        await SeedRunAsync(Active(active.Id));
        Issue publishing = await SeedAsync(Builder(2).InProgress());
        await SeedRunAsync(Publishing(publishing.Id));
        await SeedAsync(Builder(3).InProgress());
        Issue retrying = await SeedAsync(Builder(4).InProgress());
        await SeedRunAsync(Publishing(retrying.Id).FailPublish("provider down", transient: true, Now));
        await SeedAsync(Builder(5).TriageInProgress());
        await SeedAsync(Builder(6).FreshQueued());

        // Act
        IReadOnlyList<RemovalBlockingIssue> blocking = await QueryAsync(_repositoryId);

        // Assert
        blocking.ShouldBe(
        [
            new RemovalBlockingIssue(_repositoryId, 1, "in_progress"),
            new RemovalBlockingIssue(_repositoryId, 2, "in_progress"),
            new RemovalBlockingIssue(_repositoryId, 3, "in_progress"),
            new RemovalBlockingIssue(_repositoryId, 5, "triage_in_progress"),
        ]);
    }

    [Fact]
    public async Task ReportsRevisionAndReviewRunsByTheirState()
    {
        // Arrange
        await SeedAsync(Builder(1).RevisionInProgress());
        await SeedAsync(Builder(2).ReviewInProgress());

        // Act
        IReadOnlyList<RemovalBlockingIssue> blocking = await QueryAsync(_repositoryId);

        // Assert
        blocking.Select(b => b.State).ShouldBe(["revision_in_progress", "review_in_progress"]);
    }

    [Fact]
    public async Task IgnoresTheIssuesOfOtherRepositories()
    {
        // Arrange
        await SeedAsync(new IssueBuilder()
            .WithMonitoredRepositoryId(MonitoredRepositoryId.New())
            .WithIssueNumber(1)
            .WithDetectedAt(Now)
            .InProgress());

        // Act
        IReadOnlyList<RemovalBlockingIssue> blocking = await QueryAsync(_repositoryId);

        // Assert
        blocking.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoRepositoriesAreGiven_ReportsNone()
    {
        // Arrange
        await SeedAsync(Builder(1).InProgress());

        // Act
        IReadOnlyList<RemovalBlockingIssue> blocking = await QueryAsync();

        // Assert
        blocking.ShouldBeEmpty();
    }

    private IssueBuilder Builder(int issueNumber) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(_repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .WithFeedbackCutoffAt(Now)
            .WithWorkerRunId(WorkerRunId.New());

    private static ActiveRun Active(IssueId issueId) =>
        StartingRun.Begin(issueId, WorkerRunId.New()).Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/1-run"),
            MonitoredRepositoryId.New());

    private static PublishingRun Publishing(IssueId issueId) =>
        Active(issueId).BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, Now),
            containerOutput: null,
            verdict: null,
            pullRequestText: null);

    private async Task<Issue> SeedAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task SeedRunAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private Task<IReadOnlyList<RemovalBlockingIssue>> QueryAsync(params MonitoredRepositoryId[] repositoryIds) =>
        _sut.GetBlockingIssuesAsync(repositoryIds, TestContext.Current.CancellationToken);
}
