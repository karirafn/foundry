using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
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

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.InFlightWorkerCountQueryTests;

public sealed class GetCountsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetCountsAsync()
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

    private InFlightWorkerCountQuery BuildSut() => new(_dbContext, new WorkerRunQueries(_dbContext));

    private async Task SeedInProgressAsync(MonitoredRepositoryId repoId, int issueNumber)
    {
        InProgressIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(issueNumber)
            .InProgress();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task SeedRevisionInProgressAsync(MonitoredRepositoryId repoId, int issueNumber)
    {
        RevisionInProgressIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(issueNumber)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
    }

    // Cycle 1: empty database returns empty map
    [Fact]
    public async Task WhenNoInFlightIssues_ReturnsEmptyMap()
    {
        // Arrange
        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts.ShouldBeEmpty();
    }

    // Cycle 2: one InProgressIssue → count 1 for that repo
    [Fact]
    public async Task WhenOneInProgressIssue_ReturnsCountOneForThatRepo()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        await SeedInProgressAsync(repoId, issueNumber: 1);

        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts.Count.ShouldBe(1);
        counts[repoId].ShouldBe(1);
    }

    // Cycle 3: one RevisionInProgressIssue → count 1 for that repo
    [Fact]
    public async Task WhenOneRevisionInProgressIssue_ReturnsCountOneForThatRepo()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        await SeedRevisionInProgressAsync(repoId, issueNumber: 1);

        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts.Count.ShouldBe(1);
        counts[repoId].ShouldBe(1);
    }

    // Cycle 4: both InProgress and RevisionInProgress in same repo → combined count 2
    [Fact]
    public async Task WhenInProgressAndRevisionInProgressInSameRepo_ReturnsCombinedCount()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        await SeedInProgressAsync(repoId, issueNumber: 1);
        await SeedRevisionInProgressAsync(repoId, issueNumber: 2);

        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts.Count.ShouldBe(1);
        counts[repoId].ShouldBe(2);
    }

    [Fact]
    public async Task WhenATriageRunIsInFlight_ItCountsAgainstTheRepositoryLimit()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        await SeedInProgressAsync(repoId, issueNumber: 1);
        TriageInProgressIssue triaging = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(2)
            .TriageInProgress();
        _dbContext.Set<Issue>().Add(triaging);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();

        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts[repoId].ShouldBe(2);
    }

    // Cycle 5: two repos each with one in-flight → separate counts
    [Fact]
    public async Task WhenTwoReposEachWithOneInFlight_ReturnsCountPerRepo()
    {
        // Arrange
        MonitoredRepositoryId repoA = MonitoredRepositoryId.New();
        MonitoredRepositoryId repoB = MonitoredRepositoryId.New();

        await SeedInProgressAsync(repoA, issueNumber: 1);
        await SeedRevisionInProgressAsync(repoB, issueNumber: 2);

        InFlightWorkerCountQuery sut = BuildSut();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts =
            await sut.GetCountsAsync(CancellationToken.None);

        // Assert
        counts.Count.ShouldBe(2);
        counts[repoA].ShouldBe(1);
        counts[repoB].ShouldBe(1);
    }

    private static readonly DateTimeOffset ExitedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private async Task<InProgressIssue> SeedInProgressIssueAsync(MonitoredRepositoryId repoId)
    {
        InProgressIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(7)
            .InProgress();
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task SeedRunAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
    }

    private static ActiveRun ActiveRunFor(Issue issue) =>
        StartingRun.Begin(issue.Id, WorkerRunId.New()).Activate(
            ContainerId.From("container-" + Guid.NewGuid()),
            BranchName.From("feat/7-run"),
            issue.MonitoredRepositoryId);

    private static PublishingRun PublishingRunFor(Issue issue) =>
        ActiveRunFor(issue).BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, ExitedAt),
            containerOutput: null,
            verdict: null,
            pullRequestText: null);

    [Fact]
    public async Task WhenAnIssueWasJustClaimedAndHasNoRunYet_CountsIt()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        await SeedInProgressIssueAsync(repoId);

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts[repoId].ShouldBe(1);
    }

    [Fact]
    public async Task WhenARevisionWasJustClaimedAndItsOnlyRunIsCompleted_CountsIt()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revision = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(8)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(revision);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        await SeedRunAsync(ActiveRunFor(revision).Complete(exitCode: 0, branchName: null, pullRequestUrl: null));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts[repoId].ShouldBe(1);
    }

    [Fact]
    public async Task WhenAnIssuesRunIsActive_CountsIt()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        InProgressIssue issue = await SeedInProgressIssueAsync(repoId);
        await SeedRunAsync(ActiveRunFor(issue));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts[repoId].ShouldBe(1);
    }

    [Fact]
    public async Task WhenAnIssuesRunIsPublishing_DoesNotCountIt()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        InProgressIssue issue = await SeedInProgressIssueAsync(repoId);
        await SeedRunAsync(PublishingRunFor(issue));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts.ContainsKey(repoId).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAnIssuesRunWaitsToRetryAPublish_DoesNotCountIt()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        InProgressIssue issue = await SeedInProgressIssueAsync(repoId);
        await SeedRunAsync(PublishingRunFor(issue).FailPublish("provider down", transient: true, ExitedAt));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts.ContainsKey(repoId).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenOneIssuePublishesAndAnotherRuns_CountsOnlyTheRunningOne()
    {
        // Arrange
        MonitoredRepositoryId repoId = MonitoredRepositoryId.New();
        InProgressIssue publishing = await SeedInProgressIssueAsync(repoId);
        RevisionInProgressIssue running = new IssueBuilder()
            .WithMonitoredRepositoryId(repoId)
            .WithIssueNumber(9)
            .RevisionInProgress();
        _dbContext.Set<Issue>().Add(running);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        await SeedRunAsync(PublishingRunFor(publishing));
        await SeedRunAsync(ActiveRunFor(running));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, int> counts = await BuildSut().GetCountsAsync(CancellationToken.None);

        // Assert
        counts[repoId].ShouldBe(1);
    }
}
