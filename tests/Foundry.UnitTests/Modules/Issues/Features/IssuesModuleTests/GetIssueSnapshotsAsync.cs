using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueSnapshotsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetIssueSnapshotsAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(_dbContext, new NullRepositorySlugQueries(), new NullRepositoryEligibilityQuery(), new NullWorkerRunQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenNoIssuesMatch_ReturnsEmptyDictionary()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 1, 2 },
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssuesMatch_ReturnsSnapshotsKeyedByIssueNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(5)
            .WithTitle("My Issue")
            .WithBody("Issue body")
            .WithLabels(["bug", "foundry"])
            .Detected();

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 5 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        IssueSnapshot snapshot = result[5];
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.Title.ShouldBe("My Issue"),
            () => snapshot.Labels.ShouldBe(["bug", "foundry"]));
    }

    [Fact]
    public async Task WhenRequestedNumberNotInRepo_IsOmittedFromResult()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(3)
            .WithTitle("Issue 3")
            .Detected();

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            repositoryId,
            new HashSet<int> { 3, 99 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        result.ContainsKey(3).ShouldBeTrue();
        result.ContainsKey(99).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenIssuesBelongToDifferentRepo_AreOmittedFromResult()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        DetectedIssue targetIssue = new IssueBuilder()
            .WithMonitoredRepositoryId(targetRepo)
            .WithIssueNumber(1)
            .WithTitle("Target")
            .Detected();

        DetectedIssue otherIssue = new IssueBuilder()
            .WithMonitoredRepositoryId(otherRepo)
            .WithIssueNumber(1)
            .WithTitle("Other")
            .Detected();

        _dbContext.Set<Issue>().AddRange(targetIssue, otherIssue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyDictionary<int, IssueSnapshot> result = await _sut.GetIssueSnapshotsAsync(
            targetRepo,
            new HashSet<int> { 1 },
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(1);
        result[1].Title.ShouldBe("Target");
    }

}
