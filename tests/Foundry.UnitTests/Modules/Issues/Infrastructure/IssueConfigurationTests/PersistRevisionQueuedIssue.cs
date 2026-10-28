using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
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

    [Fact]
    public async Task WhenRevisionQueuedFromReview_CanBeReloadedAsRevisionQueuedIssueWithAllFields()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(55)
            .WithTitle("Revision queued issue")
            .WithLabels([])
            .WithBranchName("feat/issue-55")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .WithReviewComments(comments)
            .RevisionQueued();

        _dbContext.Set<Issue>().Add(revisionQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue reloaded = result.ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFeedback rf = reloaded.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.BranchName.ShouldBe("feat/issue-55"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/10"),
            () => rf.Comments.Count.ShouldBe(2),
            () => rf.Comments[0].Body.ShouldBe("Please fix the formatting."),
            () => rf.Comments[1].Body.ShouldBe("Rename this variable."),
            () => rf.Comments[1].FilePath.ShouldBe("src/Foo.cs"),
            () => rf.Comments[1].Line.ShouldBe(42),
            () => reloaded.Author.Value.ShouldBe("octocat"),
            () => reloaded.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public async Task WhenRevisionQueuedFromReviewWithOmittedCount_TriggerPreservesOmittedCountAndNewestCommentAt()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(57)
            .WithTitle("Revision with omitted comments")
            .WithLabels([])
            .WithBranchName("feat/issue-57")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/12")
            .Review();

        RevisionQueuedIssue revisionQueued = review.Revise(
            [new ReviewComment("Fix this.")],
            omittedCommentCount: 5,
            newestCommentAt: new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));

        _dbContext.Set<Issue>().Add(revisionQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);

        // Assert
        RevisionQueuedIssue reloaded = result.ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.ReviewFeedback rf = reloaded.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        reloaded.ShouldSatisfyAllConditions(
            () => rf.OmittedCommentCount.ShouldBe(5),
            () => rf.NewestConsumedCommentAt.ShouldBe(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenRevisionQueuedFromFailedChecks_FailingChecksSurviveRoundTrip()
    {
        // Arrange — build via the real production path: ChecksPending → FixChecks → RevisionQueuedIssue
        // with a FailedChecks trigger. This is the canonical path production uses when a
        // still_failing verdict is received and the fix-attempt cap has not been reached.
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IReadOnlyList<FailingCheck> failingChecks =
        [
            new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/99"),
            new FailingCheck("ci/lint", "failure", null),
        ];
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(70)
            .WithTitle("Failed checks revision")
            .WithLabels([])
            .WithBranchName("feat/issue-70")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/30")
            .WithFailingChecks(failingChecks)
            .ChecksPending()
            .FixChecks(failingChecks);

        _dbContext.Set<Issue>().Add(revisionQueued);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([revisionQueued.Id], TestContext.Current.CancellationToken);

        // Assert — FailedChecks trigger survives EF persistence round-trip intact.
        RevisionQueuedIssue reloaded = result.ShouldBeOfType<RevisionQueuedIssue>();
        RevisionTrigger.FailedChecks fc = reloaded.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        reloaded.ShouldSatisfyAllConditions(
            () => fc.FailingChecks.Count.ShouldBe(2),
            () => fc.FailingChecks[0].Name.ShouldBe("ci/build"),
            () => fc.FailingChecks[0].Conclusion.ShouldBe("failure"),
            () => fc.FailingChecks[0].DetailsUrl.ShouldBe("https://github.com/owner/repo/actions/runs/99"),
            () => fc.FailingChecks[1].Name.ShouldBe("ci/lint"),
            () => fc.FailingChecks[1].Conclusion.ShouldBe("failure"),
            () => fc.FailingChecks[1].DetailsUrl.ShouldBeNull(),
            () => reloaded.BranchName.ShouldBe("feat/issue-70"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/30"));
    }

    [Fact]
    public async Task WhenReviewIssueTransitioned_FeedbackCutoffAtIsPersisted()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero);
        ReviewIssue review = new IssueBuilder()
            .WithIssueNumber(56)
            .WithTitle("Review issue with cutoff")
            .WithLabels([])
            .WithBranchName("feat/issue-56")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/11")
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
        reloaded.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt);
    }
}
