using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.SpecWatchedIssueQueriesTests;

public sealed class GetSpecWatchedIssuesAsync : IAsyncDisposable
{
    private static readonly SpecHash Triaged = SpecHash.From(new string('a', 64));

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ISpecWatchedIssueQueries _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public GetSpecWatchedIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new SpecWatchedIssueQueries(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void Seed(Issue issue, SpecHash? triagedSpecHash = null)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.Entry(issue).Property(i => i.TriagedSpecHash).CurrentValue = triagedSpecHash;
        _dbContext.SaveChanges();
    }

    private static IssueBuilder Builder(MonitoredRepositoryId repositoryId, int issueNumber) =>
        new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithBranchName($"feat/{issueNumber}-x")
            .WithPullRequestUrl($"https://github.com/o/r/pull/{issueNumber}");

    [Fact]
    public async Task WhenTheRepositoryHasIssuesInEveryKindOfState_ReturnsOnlyTheSixWatchedStates()
    {
        // Arrange
        Seed(Builder(_repositoryId, 1).RevisionQueued());
        Seed(Builder(_repositoryId, 2).RevisionBlocked([20]));
        Seed(Builder(_repositoryId, 3).ContinuationQueued());
        Seed(Builder(_repositoryId, 4).ContinuationBlocked([20]));
        Seed(Builder(_repositoryId, 5).Review());
        Seed(Builder(_repositoryId, 6).RevisionFailed());
        Seed(Builder(_repositoryId, 7).RevisionInProgress());
        Seed(Builder(_repositoryId, 8).ChecksPending());
        Seed(Builder(_repositoryId, 9).MergePending());
        Seed(Builder(_repositoryId, 10).ReviewQueued());
        Seed(Builder(_repositoryId, 11).AwaitingInput());
        Seed(Builder(_repositoryId, 12).TriageQueued());

        // Act
        IReadOnlyList<SpecWatchedIssue> watched = await _sut.GetSpecWatchedIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        watched.Select(issue => issue.IssueNumber).ShouldBe([1, 2, 3, 4, 5, 6]);
    }

    [Fact]
    public async Task WhenAnIssueHasATriagedSpecHash_ReturnsIt()
    {
        // Arrange
        Seed(Builder(_repositoryId, 1).Review(), Triaged);
        Seed(Builder(_repositoryId, 2).Review());

        // Act
        IReadOnlyList<SpecWatchedIssue> watched = await _sut.GetSpecWatchedIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        watched.ShouldBe([new SpecWatchedIssue(1, Triaged), new SpecWatchedIssue(2, null)]);
    }

    [Fact]
    public async Task WhenAnotherRepositoryHasWatchedIssues_ReturnsNone()
    {
        // Arrange
        Seed(Builder(MonitoredRepositoryId.New(), 1).Review());

        // Act
        IReadOnlyList<SpecWatchedIssue> watched = await _sut.GetSpecWatchedIssuesAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        watched.ShouldBeEmpty();
    }
}
