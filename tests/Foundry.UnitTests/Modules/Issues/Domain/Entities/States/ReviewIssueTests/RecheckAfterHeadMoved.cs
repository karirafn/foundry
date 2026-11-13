using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class RecheckAfterHeadMoved
{
    private static readonly DateTimeOffset TransitionedAt = new(2026, 11, 13, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void WhenHeadMoved_ReturnsChecksPendingIssueWithSameId()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(TransitionedAt);

        // Assert
        checksPending.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenHeadMoved_EntersChecksPendingAtTransitionTime()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(TransitionedAt);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.EnteredChecksPendingAt.ShouldBe(TransitionedAt),
            () => checksPending.EnteredStateAt.ShouldBe(TransitionedAt));
    }

    [Fact]
    public void WhenHeadMoved_CarriesPullRequestFieldsFromReview()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        ReviewIssue review = new IssueBuilder()
            .WithBranchName("feat/7-moved")
            .WithPullRequestUrl("https://gitlab.com/group/project/-/merge_requests/7")
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .WithRebaseOutcome(new RebaseOutcome.Rebased())
            .Review();

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(TransitionedAt);

        // Assert
        checksPending.ShouldSatisfyAllConditions(
            () => checksPending.WorkerRunId.ShouldBe(review.WorkerRunId),
            () => checksPending.BranchName.ShouldBe("feat/7-moved"),
            () => checksPending.PullRequestUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/7"),
            () => checksPending.FeedbackCutoffAt.ShouldBe(review.FeedbackCutoffAt),
            () => checksPending.RebaseOutcome.ShouldBe(new RebaseOutcome.Rebased()),
            () => checksPending.RebaseConflictAttempts.ShouldBe(review.RebaseConflictAttempts));
    }

    [Fact]
    public void WhenReviewWasReachedAfterExhaustingFixes_ResetsChecksFixAttempts()
    {
        // Arrange
        IssueBuilder builder = new();
        ReviewIssue review = builder.ChecksPendingAtCap()
            .ToReview(new ChecksOutcome.StillFailing([]), "0a1b2c3d", TransitionedAt);

        // Act
        ChecksPendingIssue checksPending = review.RecheckAfterHeadMoved(TransitionedAt);

        // Assert
        checksPending.ChecksFixAttempts.ShouldBe(0);
    }

    [Fact]
    public void WhenHeadMoved_RaisesIssueChecksPendingDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .Review();

        // Act
        review.RecheckAfterHeadMoved(TransitionedAt);

        // Assert
        IssueChecksPending domainEvent = review.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueChecksPending>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => domainEvent.IssueNumber.ShouldBe(42),
            () => domainEvent.EnteredChecksPendingAt.ShouldBe(TransitionedAt));
    }
}
