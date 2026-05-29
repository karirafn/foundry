using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DbContextTransitionExtensionsTests;

public sealed class TransitionAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IssueTestDbContext _dbContext;

    public TransitionAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<IssueTestDbContext> options = new DbContextOptionsBuilder<IssueTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new IssueTestDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static DetectedIssue CreateDetectedIssue(MonitoredRepositoryId repositoryId) =>
        DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task WhenTransitioned_NewVariantRowExistsInDatabase()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        _dbContext.Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();

        // Act
        await _dbContext.TransitionAsync(detected, queued, TestContext.Current.CancellationToken);

        // Assert
        Issue? result = await _dbContext.Issues.FindAsync(
            [queued.Id],
            TestContext.Current.CancellationToken);
        result.ShouldBeOfType<QueuedIssue>();
        result.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public async Task WhenTransitioned_StoredEntityIsNoLongerTheOldVariantType()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        _dbContext.Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();

        // Act
        await _dbContext.TransitionAsync(detected, queued, TestContext.Current.CancellationToken);

        // Assert
        Issue? result = await _dbContext.Issues.FindAsync(
            [queued.Id],
            TestContext.Current.CancellationToken);
        result.ShouldNotBeOfType<DetectedIssue>();
    }
}
