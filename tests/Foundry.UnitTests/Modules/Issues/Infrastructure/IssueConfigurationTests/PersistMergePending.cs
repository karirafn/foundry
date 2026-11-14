using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class PersistMergePending : IAsyncDisposable
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistMergePending()
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
    public async Task WhenMergePending_FieldsRoundTrip()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        RebaseOutcome rebaseOutcome = new RebaseOutcome.Skipped("timeout");
        MergePendingIssue mergePending = new IssueBuilder()
            .WithIssueNumber(300)
            .WithBranchName("feat/300-merge")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/300")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .WithRebaseOutcome(rebaseOutcome)
            .WithVerifiedHeadSha(Head)
            .MergePending();

        _dbContext.Set<Issue>().Add(mergePending);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([mergePending.Id], TestContext.Current.CancellationToken);

        // Assert
        MergePendingIssue reloaded = result.ShouldBeOfType<MergePendingIssue>();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.WorkerRunId.ShouldBe(mergePending.WorkerRunId),
            () => reloaded.BranchName.ShouldBe("feat/300-merge"),
            () => reloaded.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/300"),
            () => reloaded.HeadSha.ShouldBe(Head),
            () => reloaded.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => reloaded.RebaseOutcome.ShouldBe(rebaseOutcome),
            () => reloaded.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => reloaded.EnteredMergePendingAt.ShouldBe(mergePending.EnteredMergePendingAt));
    }

    public static TheoryData<MergeOutcome> ReviewOutcomes() =>
    [
        new MergeOutcome.Disabled(),
        new MergeOutcome.Skipped(MergeSkipReasons.NotMergeable),
    ];

    [Theory]
    [MemberData(nameof(ReviewOutcomes))]
    public async Task WhenReviewFromMergePending_MergeOutcomeRoundTrips(MergeOutcome mergeOutcome)
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().MergePending().ToReview(mergeOutcome, DateTimeOffset.UtcNow);
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<ReviewIssue>().MergeOutcome.ShouldBe(mergeOutcome);
    }

    [Fact]
    public async Task WhenReviewNeverMergePending_MergeOutcomeIsNull()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();
        _dbContext.Set<Issue>().Add(review);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext.Set<Issue>().FindAsync([review.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<ReviewIssue>().MergeOutcome.ShouldBeNull();
    }
}
