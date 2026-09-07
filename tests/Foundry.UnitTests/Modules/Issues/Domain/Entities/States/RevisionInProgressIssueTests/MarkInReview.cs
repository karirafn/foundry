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

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview();

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

        // Act
        revisionInProgress.MarkInReview();

        // Assert
        IssueInReview domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedInReview_WithNewestConsumedComment_UsesThatTimestampAsFeedbackCutoff()
    {
        // Arrange
        DateTimeOffset newestConsumedCommentAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithNewestCommentAt(newestConsumedCommentAt)
            .RevisionInProgress();

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview();

        // Assert
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(revisionInProgress.NewestConsumedCommentAt!.Value),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => review.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => review.Title.ShouldBe(revisionInProgress.Title),
            () => review.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
    }

    [Fact]
    public void WhenMarkedInReview_WithNullNewestConsumedComment_ReturnsReviewIssueWithUtcNowFallback()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        review.FeedbackCutoffAt.ShouldBeInRange(before, after);
    }
}
