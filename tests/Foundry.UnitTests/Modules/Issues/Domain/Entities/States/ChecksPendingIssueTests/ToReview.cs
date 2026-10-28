using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class ToReview
{
    [Fact]
    public void WhenTransitionedToReview_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        ChecksOutcome outcome = new ChecksOutcome.Passed();

        // Act
        ReviewIssue review = checksPending.ToReview(outcome);

        // Assert
        review.Id.ShouldBe(checksPending.Id);
    }

    [Fact]
    public void WhenTransitionedToReview_CarriesChecksOutcome()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        ChecksOutcome outcome = new ChecksOutcome.NoneDetected();

        // Act
        ReviewIssue review = checksPending.ToReview(outcome);

        // Assert
        review.ChecksOutcome.ShouldBe(outcome);
    }

    [Fact]
    public void WhenTransitionedToReview_CarriesFeedbackCutoffAtFromSource()
    {
        // Arrange
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithFeedbackCutoffAt(feedbackCutoffAt)
            .ChecksPending();
        ChecksOutcome outcome = new ChecksOutcome.Passed();

        // Act
        ReviewIssue review = checksPending.ToReview(outcome);

        // Assert
        review.FeedbackCutoffAt.ShouldBe(checksPending.FeedbackCutoffAt);
    }

    [Fact]
    public void WhenTransitionedToReview_RaisesIssueInReviewDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ChecksPending();
        ChecksOutcome outcome = new ChecksOutcome.Passed();

        // Act
        checksPending.ToReview(outcome);

        // Assert
        IssueInReview domainEvent = checksPending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(checksPending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
