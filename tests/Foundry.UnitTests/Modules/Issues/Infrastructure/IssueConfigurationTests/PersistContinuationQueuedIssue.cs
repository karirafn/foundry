using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
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

public sealed class PersistContinuationQueuedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistContinuationQueuedIssue()
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
    public async Task WhenContinuableFailedRetried_CanBeReloadedAsContinuationQueuedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 11, 0, 0, TimeSpan.Zero);
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 72,
            title: "Continuation queued issue",
            body: "Issue body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ContinuableFailedIssue continuableFailed = inProgress.MarkContinuableFailed(
            workerRunId,
            "feat/issue-72",
            "Container OOM",
            "generic_failure",
            failedAt);
        await _dbContext.TransitionAsync(inProgress, continuableFailed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ContinuationQueuedIssue continuationQueued = continuableFailed.Retry();
        await _dbContext.TransitionAsync(continuableFailed, continuationQueued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([continuationQueued.Id], TestContext.Current.CancellationToken);

        // Assert
        ContinuationQueuedIssue reloaded = result.ShouldBeOfType<ContinuationQueuedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBe("feat/issue-72"),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
