using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class RetryReview
{
    private const string ReviewedCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void WhenTheReviewWasSkipped_QueuesTheReviewOfTheSameCommitAgain()
    {
        // Arrange
        ReviewIssue skipped = new IssueBuilder().WithVerifiedHeadSha(ReviewedCommit).ReviewSkipped();
        DateTimeOffset retriedAt = DateTimeOffset.UtcNow;

        // Act
        ReviewQueuedIssue? queued = skipped.RetryReview(retriedAt);

        // Assert
        queued.ShouldNotBeNull().ShouldSatisfyAllConditions(
            q => q.Id.ShouldBe(skipped.Id),
            q => q.ReviewedCommit.ShouldBe(ReviewedCommit),
            q => q.BranchName.ShouldBe(skipped.BranchName),
            q => q.PullRequestUrl.ShouldBe(skipped.PullRequestUrl),
            q => q.ChecksOutcome.ShouldBe(skipped.ChecksOutcome),
            q => q.EnteredStateAt.ShouldBe(retriedAt),
            q => skipped.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueReviewQueued>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenTheReviewWasSkipped_KeepsTheAutoMergeTheSkippedReviewHad(bool autoMerge)
    {
        // Arrange
        ReviewIssue skipped = new IssueBuilder().WithAutoMerge(autoMerge).ReviewSkipped();

        // Act
        ReviewQueuedIssue? queued = skipped.RetryReview(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldNotBeNull().AutoMerge.ShouldBe(autoMerge);
    }

    [Fact]
    public void WhenTheReviewerHandedThePullRequestToAPerson_ReturnsNull()
    {
        // Arrange
        RebasePendingIssue beforeReady = (RebasePendingIssue)new IssueBuilder().ReviewInProgress().ResolveVerdict(
            ReviewVerdictOutcome.ApprovedForPerson,
            patchId: null,
            new RevisionTrigger.ReviewFindings([], [], ReviewTestsAssessment.Adequate, "Covered."),
            DateTimeOffset.UtcNow);
        ReviewIssue review = (ReviewIssue)beforeReady.ResolveRebase(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow);

        // Act
        ReviewQueuedIssue? queued = review.RetryReview(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.ShouldBeNull(),
            () => review.DomainEvents.ShouldBeEmpty());
    }

    [Fact]
    public void WhenTheIssueNeverWentThroughTheReviewStep_ReturnsNull()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();

        // Act
        ReviewQueuedIssue? queued = review.RetryReview(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldBeNull();
    }
}
