using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetReviewIssuesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public GetReviewIssuesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(
            _dbContext,
            new NullRepositorySlugQueries(),
            new NullRepositoryEligibilityQuery(),
            new NullWorkerRunQueries(),
            new InFlightWorkerCountQuery(_dbContext, new NullWorkerRunQueries()),
            new NullGlobalSettingsQueries(),
            new NullMergeQueueQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static string PullRequestUrl(int number) => $"https://github.com/owner/repo/pull/{number + 100}";

    private ReviewIssue Seed(int number, bool markedReady)
    {
        ReviewIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(_repositoryId)
            .WithIssueNumber(number)
            .WithPullRequestUrl(PullRequestUrl(number))
            .Review();
        if (markedReady)
        {
            issue.RecordPullRequestMarkedReady(PullRequestUrl(number));
        }

        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    [Fact]
    public async Task ReportsWhetherEachPullRequestIsMarkedReady()
    {
        // Arrange
        Seed(1, markedReady: true);
        Seed(2, markedReady: false);

        // Act
        IReadOnlyList<ReviewIssueInfo> issues = await _sut.GetReviewIssuesAsync(_repositoryId, TestContext.Current.CancellationToken);

        // Assert
        issues.Single(i => i.IssueNumber == 1).IsPullRequestMarkedReady.ShouldBeTrue();
        issues.Single(i => i.IssueNumber == 2).IsPullRequestMarkedReady.ShouldBeFalse();
    }
}
