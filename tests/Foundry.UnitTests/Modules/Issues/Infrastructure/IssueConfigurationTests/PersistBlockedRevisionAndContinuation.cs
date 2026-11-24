using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistBlockedRevisionAndContinuation : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistBlockedRevisionAndContinuation()
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

    [Fact]
    public async Task WhenARevisionIsHeldByBlockers_CanBeReloadedAsRevisionBlockedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset newestCommentAt = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        RevisionBlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(73)
            .WithBranchName("feat/73-held")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/173")
            .WithNewestCommentAt(newestCommentAt)
            .RevisionBlocked([5, 8]);

        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([blocked.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionBlockedIssue reloaded = result.ShouldBeOfType<RevisionBlockedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBe("feat/73-held"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/173"),
            () => reloaded.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>().NewestConsumedCommentAt.ShouldBe(newestCommentAt),
            () => reloaded.BlockedBy.ShouldBe([5, 8]),
            () => reloaded.HasPersonFeedback.ShouldBeTrue(),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public async Task WhenAContinuationIsHeldByBlockers_CanBeReloadedAsContinuationBlockedWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationBlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(74)
            .WithBranchName("feat/74-held")
            .WithFailureReason("Container OOM")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .ContinuationBlocked([6]);

        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([blocked.Id], TestContext.Current.CancellationToken);

        // Assert
        ContinuationBlockedIssue reloaded = result.ShouldBeOfType<ContinuationBlockedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBe("feat/74-held"),
            () => reloaded.FailureReason.ShouldBe("Container OOM"),
            () => reloaded.BlockedBy.ShouldBe([6]),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public async Task WhenAQueuedRevisionsPullRequestClosed_TheFailedIssueReloadsWithoutARun()
    {
        // Arrange
        ContinuableFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(75)
            .RevisionQueued()
            .Fail("Pull request closed without merge", FailureCategory.PrClosed, DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([failed.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<ContinuableFailedIssue>().WorkerRunId.ShouldBeNull();
    }
}
