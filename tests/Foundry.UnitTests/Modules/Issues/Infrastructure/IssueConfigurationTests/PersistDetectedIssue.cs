using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistDetectedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistDetectedIssue()
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

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    [Fact]
    public async Task WhenDetectedIssuePersisted_CanBeReloadedAsDetectedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "Fix the bug",
            body: "Body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry", "bug"],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([issue.Id], TestContext.Current.CancellationToken);

        // Assert
        DetectedIssue detected = result.ShouldBeOfType<DetectedIssue>();
        detected.ShouldSatisfyAllConditions(
            () => detected.Id.ShouldBe(issue.Id),
            () => detected.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => detected.IssueNumber.ShouldBe(42),
            () => detected.Title.ShouldBe("Fix the bug"),
            () => detected.Author.Value.ShouldBe("octocat"),
            () => detected.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")),
            () => detected.Labels.ShouldBe(["foundry", "bug"]));
    }

    [Fact]
    public async Task WhenDetectedIssuePersistedWithNonDefaultIssueKind_ReloadsWithCorrectIssueKind()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 77,
            title: "Fix the crash",
            body: "Body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry", "bug"],
            detectedAt: DateTimeOffset.UtcNow,
            issueKind: IssueKind.Bug);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([issue.Id], TestContext.Current.CancellationToken);

        // Assert
        DetectedIssue detected = result.ShouldBeOfType<DetectedIssue>();
        detected.IssueKind.ShouldBe(IssueKind.Bug);
    }

    [Fact]
    public async Task WhenDetectedIssuePersistedWithoutIssueKind_ReloadsAsFeature()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 78,
            title: "Add a feature",
            body: "Body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([issue.Id], TestContext.Current.CancellationToken);

        // Assert
        DetectedIssue detected = result.ShouldBeOfType<DetectedIssue>();
        detected.IssueKind.ShouldBe(IssueKind.Feature);
    }

    [Fact]
    public async Task WhenDetectedIssuePersistedWithEmptyBlockedBy_ReloadsAsEmptyList()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 99,
            title: "No blockers",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([issue.Id], TestContext.Current.CancellationToken);

        // Assert — BlockedBy must come back as empty list, not null
        DetectedIssue detected = result.ShouldBeOfType<DetectedIssue>();
        detected.BlockedBy.ShouldNotBeNull();
        detected.BlockedBy.ShouldBeEmpty();
    }
}
