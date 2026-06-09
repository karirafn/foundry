using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistContinuableFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistContinuableFailedIssue()
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
    public async Task WhenContinuableFailedIssueTransitioned_CanBeReloadedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 70,
            title: "Continuable failed issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid continuableWorkerRunId = Guid.NewGuid();
        ContinuableFailedIssue continuable = inProgress.MarkContinuableFailed(
            continuableWorkerRunId,
            "foundry/70/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            failedAt);
        await _dbContext.TransitionAsync(inProgress, continuable, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([continuable.Id], TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue reloaded = result.ShouldBeOfType<ContinuableFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(continuableWorkerRunId),
            () => reloaded.BranchName.ShouldBe("foundry/70/add-feature"),
            () => reloaded.LatestProgress.ShouldBe("Implemented the core feature"),
            () => reloaded.FailureReason.ShouldBe("Container exited with code 1"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
