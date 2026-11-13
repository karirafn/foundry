using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
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

public sealed class GetUnpublishedRunIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetUnpublishedRunIssueNumbersAsync()
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
            new InFlightWorkerCountQuery(_dbContext),
            new NullGlobalSettingsQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void Seed(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
    }

    private static IssueBuilder Builder(MonitoredRepositoryId repositoryId, int issueNumber) =>
        new IssueBuilder().WithMonitoredRepositoryId(repositoryId).WithIssueNumber(issueNumber);

    [Fact]
    public async Task WhenRepositoryHasPublishBlockedAndPublishFailedIssues_ReturnsBothNumbers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(Builder(repositoryId, 1).PublishBlocked());
        Seed(Builder(repositoryId, 2).PublishFailedFromRevision());

        // Act
        IReadOnlySet<int> numbers = await _sut.GetUnpublishedRunIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        numbers.ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenIssuesAreInOtherStates_ExcludesThem()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(Builder(repositoryId, 1).Review());
        Seed(Builder(repositoryId, 2).InProgress());
        Seed(Builder(repositoryId, 3).Completed());
        Seed(Builder(repositoryId, 4).ContinuableFailed());

        // Act
        IReadOnlySet<int> numbers = await _sut.GetUnpublishedRunIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        numbers.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAnotherRepositoryHasAPublishBlockedIssue_ExcludesIt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        Seed(Builder(MonitoredRepositoryId.New(), 1).PublishBlocked());

        // Act
        IReadOnlySet<int> numbers = await _sut.GetUnpublishedRunIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        numbers.ShouldBeEmpty();
    }
}
