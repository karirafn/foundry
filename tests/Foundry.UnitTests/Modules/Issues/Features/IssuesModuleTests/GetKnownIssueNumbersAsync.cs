using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetKnownIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetKnownIssueNumbersAsync()
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    [Fact]
    public async Task WhenNoIssuesExist_ReturnsEmptySet()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlySet<int> result = await _sut.GetKnownIssueNumbersAsync(
            repositoryId,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssuesExistForRepository_ReturnsTheirNumbers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        DetectedIssue issue1 = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Issue 1",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        DetectedIssue issue2 = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 2,
            title: "Issue 2",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().AddRange(issue1, issue2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlySet<int> result = await _sut.GetKnownIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenIssuesExistForDifferentRepository_ReturnsOnlyMatchingNumbers()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        DetectedIssue targetIssue = DetectedIssue.Detect(
            targetRepo,
            issueNumber: 10,
            title: "Target",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        DetectedIssue otherIssue = DetectedIssue.Detect(
            otherRepo,
            issueNumber: 20,
            title: "Other",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().AddRange(targetIssue, otherIssue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlySet<int> result = await _sut.GetKnownIssueNumbersAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([10], ignoreOrder: true);
    }

}
