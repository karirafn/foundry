using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.TriageFailedIssueQueriesTests;

public sealed class GetTriageFailedIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ITriageFailedIssueQueries _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public GetTriageFailedIssueNumbersAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new TriageFailedIssueQueries(_dbContext);
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
    public async Task WhenTheRepositoryHasIssuesInSeveralStates_ReturnsOnlyTheTriageFailedNumbers()
    {
        // Arrange
        Seed(Builder(_repositoryId, 1).TriageFailed());
        Seed(Builder(_repositoryId, 2).TriageQueued());
        Seed(Builder(_repositoryId, 3).Failed());
        Seed(Builder(_repositoryId, 4).TriageFailed());

        // Act
        IReadOnlySet<int> numbers = await _sut.GetTriageFailedIssueNumbersAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        numbers.ShouldBe([1, 4], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAnotherRepositoryHasATriageFailedIssue_ExcludesIt()
    {
        // Arrange
        Seed(Builder(MonitoredRepositoryId.New(), 1).TriageFailed());

        // Act
        IReadOnlySet<int> numbers = await _sut.GetTriageFailedIssueNumbersAsync(
            _repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        numbers.ShouldBeEmpty();
    }
}
