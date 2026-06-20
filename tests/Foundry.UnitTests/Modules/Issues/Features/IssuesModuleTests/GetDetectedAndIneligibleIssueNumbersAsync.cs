using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetDetectedAndIneligibleIssueNumbersAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;

    public GetDetectedAndIneligibleIssueNumbersAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new IssueQueries(_dbContext, new NullRepositorySlugQueries());
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static DetectedIssue CreateDetectedIssue(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        return DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task WhenNoIssuesExist_ReturnsEmptyList()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        IReadOnlyList<int> result = await _sut.GetDetectedAndIneligibleIssueNumbersAsync(
            repositoryId,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDetectedIssueExists_ReturnsItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId, issueNumber: 7);
        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<int> result = await _sut.GetDetectedAndIneligibleIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([7], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenQueuedIssueExists_DoesNotReturnItsNumber()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId, issueNumber: 9);
        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<int> result = await _sut.GetDetectedAndIneligibleIssueNumbersAsync(
            repositoryId,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssuesExistForDifferentRepository_ReturnsOnlyMatchingRepository()
    {
        // Arrange
        MonitoredRepositoryId targetRepo = MonitoredRepositoryId.New();
        MonitoredRepositoryId otherRepo = MonitoredRepositoryId.New();

        DetectedIssue targetIssue = CreateDetectedIssue(targetRepo, issueNumber: 42);
        DetectedIssue otherIssue = CreateDetectedIssue(otherRepo, issueNumber: 99);
        _dbContext.Set<Issue>().AddRange(targetIssue, otherIssue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        IReadOnlyList<int> result = await _sut.GetDetectedAndIneligibleIssueNumbersAsync(
            targetRepo,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([42], ignoreOrder: true);
    }
}
