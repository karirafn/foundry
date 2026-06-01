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

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    [Fact]
    public async Task WhenReviewIssueTransitioned_CanBeReloadedAsReviewIssueWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 42,
            title: "Review issue",
            body: "Review body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, TestContext.Current.CancellationToken);

        Guid reviewWorkerRunId = Guid.NewGuid();
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        ReviewIssue review = inProgress.MarkInReview(reviewWorkerRunId, "feat/issue-42", "https://github.com/owner/repo/pull/1", feedbackCutoffAt);
        await _dbContext.TransitionAsync(inProgress, review, TestContext.Current.CancellationToken);
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
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.Url.Value.ShouldBe(ValidUrl.Value));
    }
}
