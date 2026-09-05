using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionFailedIssueTests;

public sealed class Retry
{
    private static readonly IReadOnlyList<ReviewComment> ReviewComments =
    [
        new ReviewComment("Please fix the formatting."),
        new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
    ];

    [Fact]
    public void WhenRetried_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments(ReviewComments)
            .RevisionFailed();

        // Act
        RevisionQueuedIssue revisionQueued = failed.Retry();

        // Assert
        revisionQueued.Id.ShouldBe(failed.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueRevisionQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments(ReviewComments)
            .RevisionFailed();

        // Act
        failed.Retry();

        // Assert
        IssueRevisionQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_PreservesBranchContextAndReviewComments()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithReviewComments(ReviewComments)
            .RevisionFailed();

        // Act
        RevisionQueuedIssue revisionQueued = failed.Retry();

        // Assert
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.BranchName.ShouldBe(failed.BranchName),
            () => revisionQueued.PullRequestUrl.ShouldBe(failed.PullRequestUrl),
            () => revisionQueued.ReviewComments.Count.ShouldBe(2),
            () => revisionQueued.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => revisionQueued.ReviewComments[1].Body.ShouldBe("Rename this variable."),
            () => revisionQueued.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => revisionQueued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => revisionQueued.Title.ShouldBe(failed.Title),
            () => revisionQueued.DetectedAt.ShouldBe(failed.DetectedAt));
    }
}
