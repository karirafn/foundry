using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistReviewToFailedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistReviewToFailedIssue()
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
    public async Task WhenReviewIssueFailedTransitioned_CanBeReloadedAsContinuableFailedIssueWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId reviewWorkerRunId = WorkerRunId.New();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        ContinuableFailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(55)
            .WithTitle("Review to failed issue")
            .WithBody("PR rejected body")
            .WithLabels([])
            .WithWorkerRunId(reviewWorkerRunId)
            .WithBranchName("feat/issue-55")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/7")
            .WithFailureReason("PR was closed without merge")
            .WithFailureCategory("pr_closed")
            .WithFailedAt(failedAt)
            .ContinuableFailedFromReview();

        _dbContext.Set<Issue>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([failed.Id], TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue reloaded = result.ShouldBeOfType<ContinuableFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(reviewWorkerRunId),
            () => reloaded.FailureReason.ShouldBe("PR was closed without merge"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.BranchName.ShouldBe("feat/issue-55"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
