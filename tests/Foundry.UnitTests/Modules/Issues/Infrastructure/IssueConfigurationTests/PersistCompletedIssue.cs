using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistCompletedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistCompletedIssue()
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

    private async Task<InProgressIssue> BuildInProgressIssue(int issueNumber)
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: issueNumber,
            title: $"Issue {issueNumber}",
            body: "Body",
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

        return inProgress;
    }

    [Fact]
    public async Task WhenCompletedFromUnchanged_CanBeReloadedAsCompletedIssueWithNullPrFields()
    {
        // Arrange
        DateTimeOffset completedAt = new DateTimeOffset(2026, 5, 30, 14, 0, 0, TimeSpan.Zero);
        InProgressIssue inProgress = await BuildInProgressIssue(issueNumber: 45);

        UnchangedIssue unchanged = inProgress.MarkUnchanged(Guid.NewGuid());
        await _dbContext.TransitionAsync(inProgress, unchanged, TestContext.Current.CancellationToken);

        CompletedIssue completed = unchanged.Complete(completedAt);
        await _dbContext.TransitionAsync(unchanged, completed, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([completed.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedIssue reloaded = result.ShouldBeOfType<CompletedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.CompletedAt.ShouldBe(completedAt),
            () => reloaded.BranchName.ShouldBeNull(),
            () => reloaded.PullRequestUrl.ShouldBeNull(),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.Url.Value.ShouldBe(ValidUrl.Value));
    }

    [Fact]
    public async Task WhenCompletedFromReview_CanBeReloadedAsCompletedIssueWithPrFields()
    {
        // Arrange
        DateTimeOffset completedAt = new DateTimeOffset(2026, 5, 30, 15, 0, 0, TimeSpan.Zero);
        InProgressIssue inProgress = await BuildInProgressIssue(issueNumber: 46);

        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-46",
            "https://github.com/owner/repo/pull/2");
        await _dbContext.TransitionAsync(inProgress, review, TestContext.Current.CancellationToken);

        CompletedIssue completed = review.Complete(completedAt);
        await _dbContext.TransitionAsync(review, completed, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([completed.Id], TestContext.Current.CancellationToken);

        // Assert
        CompletedIssue reloaded = result.ShouldBeOfType<CompletedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.CompletedAt.ShouldBe(completedAt),
            () => reloaded.BranchName.ShouldBe("feat/issue-46"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.Url.Value.ShouldBe(ValidUrl.Value));
    }
}
