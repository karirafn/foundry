using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionInProgressIssueTests;

public sealed class FromRevisionQueued
{
    [Fact]
    public void WhenClaimed_ReturnsRevisionInProgressIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();
        Guid workerRunId = Guid.NewGuid();

        // Act
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId);

        // Assert
        revisionInProgress.Id.ShouldBe(revisionQueued.Id);
    }

    [Fact]
    public void WhenClaimed_RevisionInProgressIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();
        Guid workerRunId = Guid.NewGuid();

        // Act
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(workerRunId);

        // Assert
        revisionInProgress.ShouldSatisfyAllConditions(
            () => revisionInProgress.WorkerRunId.ShouldBe(workerRunId),
            () => revisionInProgress.BranchName.ShouldBe(revisionQueued.BranchName),
            () => revisionInProgress.PullRequestUrl.ShouldBe(revisionQueued.PullRequestUrl),
            () => revisionInProgress.ReviewComments.Count.ShouldBe(1),
            () => revisionInProgress.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => revisionInProgress.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => revisionInProgress.IssueNumber.ShouldBe(revisionQueued.IssueNumber),
            () => revisionInProgress.Title.ShouldBe(revisionQueued.Title),
            () => revisionInProgress.Body.ShouldBe(revisionQueued.Body),
            () => revisionInProgress.DetectedAt.ShouldBe(revisionQueued.DetectedAt));
    }

    [Fact]
    public void WhenClaimed_RaisesIssueRevisionInProgressDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();

        // Act
        revisionQueued.Claim(Guid.NewGuid());

        // Assert
        IssueRevisionInProgress domainEvent = revisionQueued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionQueued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
