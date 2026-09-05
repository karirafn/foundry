using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class Revise
{
    [Fact]
    public void WhenRevised_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix the formatting.")];

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(comments);

        // Assert
        revisionQueued.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenRevised_RaisesIssueRevisionQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please fix the formatting.")];

        // Act
        review.Revise(comments);

        // Assert
        IssueRevisionQueued domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRevised_RevisionQueuedIssueHasBranchNameAndPullRequestUrlAndReviewComments()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(comments);

        // Assert
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.BranchName.ShouldBe(review.BranchName),
            () => revisionQueued.PullRequestUrl.ShouldBe(review.PullRequestUrl),
            () => revisionQueued.ReviewComments.Count.ShouldBe(2),
            () => revisionQueued.ReviewComments[0].Body.ShouldBe("Please fix the formatting."),
            () => revisionQueued.ReviewComments[1].Body.ShouldBe("Rename this variable."),
            () => revisionQueued.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRevisedWithOmittedCount_OmittedCommentCountIsStored()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Fix this.")];

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(comments, omittedCommentCount: 3);

        // Assert
        revisionQueued.OmittedCommentCount.ShouldBe(3);
    }

    [Fact]
    public void WhenRevisedWithNewestCommentAt_NewestConsumedCommentAtIsStored()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Fix this.")];
        DateTimeOffset newestCommentAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(comments, newestCommentAt: newestCommentAt);

        // Assert
        revisionQueued.NewestConsumedCommentAt.ShouldBe(newestCommentAt);
    }

    [Fact]
    public void WhenRevisedWithoutOptionalParams_DefaultsAreUsed()
    {
        // Arrange
        ReviewIssue review = new IssueBuilder().Review();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Fix this.")];

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(comments);

        // Assert
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.OmittedCommentCount.ShouldBe(0),
            () => revisionQueued.NewestConsumedCommentAt.ShouldBeNull());
    }
}
