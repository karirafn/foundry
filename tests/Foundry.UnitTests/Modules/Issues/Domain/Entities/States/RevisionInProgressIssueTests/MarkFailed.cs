using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

public sealed class MarkFailed
{
    [Fact]
    public void WhenMarkedFailed_ReturnsRevisionFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        RevisionFailedIssue failed = revisionInProgress.MarkFailed(
            "Container exited with code 1",
            "generic_failure",
            failedAt);

        // Assert
        failed.Id.ShouldBe(revisionInProgress.Id);
    }

    [Fact]
    public void WhenMarkedFailed_RaisesIssueRevisionFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .RevisionInProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        revisionInProgress.MarkFailed("Container exited with code 1", "generic_failure", failedAt);

        // Assert
        IssueRevisionFailed domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedFailed_RevisionFailedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments([new ReviewComment("Please fix.")])
            .RevisionInProgress();
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 1, 14, 30, 0, TimeSpan.Zero);

        // Act
        RevisionFailedIssue failed = revisionInProgress.MarkFailed(
            failureReason,
            "generic_failure",
            failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => failed.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => failed.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => failed.ReviewComments.Count.ShouldBe(1),
            () => failed.ReviewComments[0].Body.ShouldBe("Please fix."),
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => failed.Title.ShouldBe(revisionInProgress.Title),
            () => failed.Body.ShouldBe(revisionInProgress.Body),
            () => failed.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
    }
}
