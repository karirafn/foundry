using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
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

    private InFlightWorkerCountQuery BuildSut() => new(_dbContext);

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
}
