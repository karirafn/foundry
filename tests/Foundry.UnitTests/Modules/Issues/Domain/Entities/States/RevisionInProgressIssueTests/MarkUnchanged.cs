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
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkUnchanged(feedbackCutoffAt);

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
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        revisionInProgress.MarkUnchanged(feedbackCutoffAt);

        // Assert
        IssueInReview domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedUnchanged_ReturnsReviewIssueNotUnchangedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkUnchanged(feedbackCutoffAt);

        // Assert — PR still exists, so returns to ReviewIssue (not UnchangedIssue)
        review.ShouldSatisfyAllConditions(
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
