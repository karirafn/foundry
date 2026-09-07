using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

public sealed class MarkUnchanged
{
    [Fact]
    public void WhenMarkedUnchanged_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        ReviewIssue review = revisionInProgress.MarkUnchanged();

        // Assert
        review.Id.ShouldBe(revisionInProgress.Id);
    }

    [Fact]
    public void WhenMarkedUnchanged_RaisesIssueInReviewDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();

        // Act
        revisionInProgress.MarkUnchanged();

        // Assert
        IssueInReview domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedUnchanged_WithNewestConsumedComment_UsesThatTimestampAsFeedbackCutoff()
    {
        // Arrange
        DateTimeOffset newestConsumedCommentAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithNewestCommentAt(newestConsumedCommentAt)
            .RevisionInProgress();

        // Act
        ReviewIssue review = revisionInProgress.MarkUnchanged();

        // Assert — PR still exists, so returns to ReviewIssue (not UnchangedIssue)
        review.ShouldSatisfyAllConditions(
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(revisionInProgress.NewestConsumedCommentAt!.Value),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedUnchanged_WithNullNewestConsumedComment_ReturnsReviewIssueWithUtcNowFallback()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkUnchanged();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        review.ShouldNotBeNull();
        review.FeedbackCutoffAt.ShouldBeInRange(before, after);
    }
}
