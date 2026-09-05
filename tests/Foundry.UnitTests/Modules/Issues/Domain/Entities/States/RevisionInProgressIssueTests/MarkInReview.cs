using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

public sealed class MarkInReview
{
    [Fact]
    public void WhenMarkedInReview_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        review.Id.ShouldBe(revisionInProgress.Id);
    }

    [Fact]
    public void WhenMarkedInReview_RaisesIssueInReviewDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        IssueInReview domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedInReview_ReviewIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => review.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => review.Title.ShouldBe(revisionInProgress.Title),
            () => review.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
    }
}
