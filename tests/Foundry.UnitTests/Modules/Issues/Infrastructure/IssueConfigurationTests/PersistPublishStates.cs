using Foundry.Modules.Issues.Contracts;
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

public sealed class PersistPublishStates : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistPublishStates()
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

    private async Task<Issue?> SaveAndReloadAsync(Issue issue)
    {
        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenImplementPublishFailed_CanBeReloadedWithAllFields()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset failedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        PublishFailedIssue publishFailed = new IssueBuilder()
            .WithIssueNumber(71)
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/71-x")
            .WithFailureReason("remote unreachable")
            .WithFailedAt(failedAt)
            .PublishFailed();

        // Act
        Issue? result = await SaveAndReloadAsync(publishFailed);

        // Assert
        PublishFailedIssue reloaded = result.ShouldBeOfType<PublishFailedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/71-x"),
            () => reloaded.FailureReason.ShouldBe("remote unreachable"),
            () => reloaded.FailedAt.ShouldBe(failedAt),
            () => reloaded.IsRevision.ShouldBeFalse(),
            () => reloaded.Trigger.ShouldBeNull(),
            () => reloaded.LinkedPullRequestUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRevisionPublishFailed_CanBeReloadedWithTheRevisionContext()
    {
        // Arrange
        PublishFailedIssue publishFailed = new IssueBuilder()
            .WithIssueNumber(72)
            .WithBranchName("feat/72-x")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/72")
            .WithReviewComments([new ReviewComment("Please fix.")])
            .PublishFailedFromRevision();

        // Act
        Issue? result = await SaveAndReloadAsync(publishFailed);

        // Assert
        PublishFailedIssue reloaded = result.ShouldBeOfType<PublishFailedIssue>();
        RevisionTrigger.ReviewFeedback trigger = reloaded.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.IsRevision.ShouldBeTrue(),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/72"),
            () => trigger.Comments.ShouldHaveSingleItem().Body.ShouldBe("Please fix."));
    }

    [Fact]
    public async Task WhenBranchDiverged_CanBeReloadedWithBothBranches()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        BranchDivergedIssue diverged = new IssueBuilder()
            .WithIssueNumber(73)
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/73-x")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/73")
            .BranchDivergedFromRevision("feat/73-x-run-abc");

        // Act
        Issue? result = await SaveAndReloadAsync(diverged);

        // Assert
        BranchDivergedIssue reloaded = result.ShouldBeOfType<BranchDivergedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/73-x"),
            () => reloaded.SideBranchName.ShouldBe("feat/73-x-run-abc"),
            () => reloaded.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/73"),
            () => reloaded.FromRevision.ShouldBeTrue());
    }
}
