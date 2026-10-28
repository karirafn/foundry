using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistChecksPendingIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistChecksPendingIssue()
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
    public async Task WhenChecksPendingTransitioned_CanBeReloadedAsChecksPendingIssueWithAllFields()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithIssueNumber(42)
            .WithTitle("Checks pending issue")
            .WithLabels([])
            .WithWorkerRunId(workerRunId)
            .WithBranchName("feat/issue-42")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .ChecksPending();

        _dbContext.Set<Issue>().Add(checksPending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([checksPending.Id], TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue reloaded = result.ShouldBeOfType<ChecksPendingIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.BranchName.ShouldBe("feat/issue-42"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/1"),
            () => reloaded.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => reloaded.Author.Value.ShouldBe("octocat"));
    }

    [Fact]
    public async Task WhenChecksPendingTransitioned_EnteredChecksPendingAtIsPersisted()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithIssueNumber(43)
            .WithTitle("Checks pending with entry time")
            .WithLabels([])
            .WithBranchName("feat/issue-43")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .ChecksPending();

        DateTimeOffset savedAt = checksPending.EnteredChecksPendingAt;

        _dbContext.Set<Issue>().Add(checksPending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([checksPending.Id], TestContext.Current.CancellationToken);

        // Assert
        ChecksPendingIssue reloaded = result.ShouldBeOfType<ChecksPendingIssue>();
        reloaded.EnteredChecksPendingAt.ShouldBe(savedAt);
    }
}
