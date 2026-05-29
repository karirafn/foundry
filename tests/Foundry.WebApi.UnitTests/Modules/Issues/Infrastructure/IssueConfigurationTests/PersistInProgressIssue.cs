using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistInProgressIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistInProgressIssue()
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
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    [Fact]
    public async Task WhenInProgressIssueTransitioned_CanBeReloadedAsInProgressIssueWithWorkerRunId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "In-progress issue",
            body: "In-progress body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, TestContext.Current.CancellationToken);

        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        await _dbContext.TransitionAsync(queued, inProgress, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([inProgress.Id], TestContext.Current.CancellationToken);

        // Assert
        InProgressIssue reloaded = result.ShouldBeOfType<InProgressIssue>();
        reloaded.WorkerRunId.ShouldBe(inProgress.WorkerRunId);
    }
}
