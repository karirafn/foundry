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

public sealed class PersistRevisionQueuedIssue : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRevisionQueuedIssue()
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
    public async Task WhenRevisionQueuedFromReview_CanBeReloadedAsRevisionQueuedIssueWithAllFields()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 55,
            title: "Revision queued issue",
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

        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-55",
            "https://github.com/owner/repo/pull/10",
            feedbackCutoffAt);
        await _dbContext.TransitionAsync(inProgress, review, TestContext.Current.CancellationToken);

        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        await _dbContext.TransitionAsync(review, revisionQueued, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue reloaded = result.ShouldBeOfType<RevisionQueuedIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBe("feat/issue-55"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => reloaded.ReviewComments.Count.ShouldBe(2),
            () => reloaded.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => reloaded.ReviewComments[1].Body.ShouldBe("Rename this variable."),
            () => reloaded.ReviewComments[1].FilePath.ShouldBe("src/Foo.cs"),
            () => reloaded.ReviewComments[1].Line.ShouldBe(42),
            () => reloaded.Author.Value.ShouldBe(ValidAuthor.Value),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public async Task WhenReviewIssueTransitioned_FeedbackCutoffAtIsPersisted()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 56,
            title: "Review issue with cutoff",
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

        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "feat/issue-56",
            "https://github.com/owner/repo/pull/11",
            feedbackCutoffAt);
        await _dbContext.TransitionAsync(inProgress, review, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewIssue reloaded = result.ShouldBeOfType<ReviewIssue>();
        reloaded.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt);
    }
}
