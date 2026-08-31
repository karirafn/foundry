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

public sealed class GetDependencyGraphAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetDependencyGraphAsync()
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
    public async Task WhenNoIssuesHaveBlockers_ReturnsEmptyList()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithTitle("Issue 1")
            .Detected();

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<DependencyEdge> result = await _sut.GetDependencyGraphAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueHasBlockers_ReturnsEdgePerBlocker()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        BlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(5)
            .WithTitle("Issue 5")
            .Detected()
            .Block([10, 20]);

        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<DependencyEdge> result = await _sut.GetDependencyGraphAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(new DependencyEdge(5, 10));
        result.ShouldContain(new DependencyEdge(5, 20));
    }

    [Fact]
    public async Task WhenIssuesBelongToDifferentRepo_OnlyReturnsEdgesForTargetRepo()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        BlockedIssue blockedInTarget = new IssueBuilder()
            .WithMonitoredRepositoryId(targetRepo)
            .WithIssueNumber(1)
            .WithTitle("Issue 1")
            .Detected()
            .Block([99]);

        BlockedIssue blockedInOther = new IssueBuilder()
            .WithMonitoredRepositoryId(otherRepo)
            .WithIssueNumber(2)
            .WithTitle("Issue 2")
            .Detected()
            .Block([88]);

        _dbContext.Set<Issue>().AddRange(blockedInTarget, blockedInOther);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<DependencyEdge> result = await _sut.GetDependencyGraphAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldHaveSingleItem();
        result.ShouldContain(new DependencyEdge(1, 99));
    }

}
