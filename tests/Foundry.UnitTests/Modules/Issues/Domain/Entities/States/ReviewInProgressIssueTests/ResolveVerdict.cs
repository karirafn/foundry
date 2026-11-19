using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewInProgressIssueTests;

public sealed class ResolveVerdict
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    [Fact]
    public void WhenApprovedOnPassedChecksWithAutoMerge_ReturnsMergePendingPinnedToTheReviewedCommit()
    {
        // Arrange
        WorkerRunId reviewRun = WorkerRunId.New();
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithWorkerRunId(reviewRun)
            .WithVerifiedHeadSha(Head)
            .WithAutoMerge(true)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, DateTimeOffset.UtcNow);

        // Assert
        MergePendingIssue mergePending = next.ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.HeadSha.ShouldBe(Head),
            () => mergePending.ChecksOutcome.ShouldBeOfType<ChecksOutcome.Passed>(),
            () => mergePending.BranchName.ShouldBe(reviewing.BranchName),
            () => mergePending.PullRequestUrl.ShouldBe(reviewing.PullRequestUrl),
            () => mergePending.WorkerRunId.ShouldBe(reviewRun),
            () => reviewing.DomainEvents.OfType<IssueMergePending>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenApprovedWithoutAutoMerge_ReturnsReview()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithVerifiedHeadSha(Head).WithAutoMerge(false).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, DateTimeOffset.UtcNow);

        // Assert
        ReviewIssue review = next.ShouldBeOfType<ReviewIssue>();
        review.ShouldSatisfyAllConditions(
            () => review.VerifiedHeadSha.ShouldBe(Head),
            () => review.ReviewOutcome.ShouldBeNull(),
            () => reviewing.DomainEvents.OfType<IssueInReview>().ShouldHaveSingleItem());
    }

    [Fact]
    public void WhenApprovedOnNoChecksWithAutoMerge_ReturnsReviewBecauseAPullRequestWithoutChecksNeverMergesItself()
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithChecksOutcome(new ChecksOutcome.NoneDetected())
            .WithAutoMerge(true)
            .ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ChecksOutcome.ShouldBeOfType<ChecksOutcome.NoneDetected>();
    }

    [Fact]
    public void WhenApprovedAfterPersonFeedback_ReturnsReview()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please rename this.")])
            .ChecksPendingFromRevision();
        ReviewInProgressIssue reviewing = ((ReviewQueuedIssue)checksPending.ResolveVerdict(
                new ChecksOutcome.Passed(),
                autoMerge: true,
                reviewPullRequests: true,
                Head,
                DateTimeOffset.UtcNow))
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);

        // Act
        Issue next = reviewing.ResolveVerdict(ReviewVerdictOutcome.Approved, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().HasPersonFeedback.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ReviewVerdictOutcome.ApprovedForPerson)]
    [InlineData(ReviewVerdictOutcome.ChangesRequested)]
    public void WhenNotApproved_ReturnsReviewEvenWithAutoMerge(ReviewVerdictOutcome outcome)
    {
        // Arrange
        ReviewInProgressIssue reviewing = new IssueBuilder().WithAutoMerge(true).ReviewInProgress();

        // Act
        Issue next = reviewing.ResolveVerdict(outcome, DateTimeOffset.UtcNow);

        // Assert
        next.ShouldBeOfType<ReviewIssue>().ReviewOutcome.ShouldBeNull();
    }
}
