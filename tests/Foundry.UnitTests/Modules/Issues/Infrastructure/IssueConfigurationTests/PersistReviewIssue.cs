using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistReviewIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistReviewIssue()
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
    public async Task WhenReviewIssueTransitioned_CanBeReloadedAsReviewIssueWithAllFields()
    {
        // Arrange
        Guid reviewWorkerRunId = Guid.NewGuid();
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(42)
            .WithTitle("Review issue")
            .WithBody("Review body")
            .WithLabels([])
            .WithWorkerRunId(reviewWorkerRunId)
            .WithBranchName("feat/issue-42")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/1")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .Review();

        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(reviewWorkerRunId),
            () => reloaded.BranchName.ShouldBe("feat/issue-42"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/1"),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.Url.Value.ShouldBe(new Uri("https://github.com/owner/repo/issues/1")));
    }
}
