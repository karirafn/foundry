using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class Revise
{
    [Fact]
    public void WhenRevised_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please add tests.")];

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.Revise(comments);

        // Assert
        revisionQueued.Id.ShouldBe(checksPending.Id);
    }

    [Fact]
    public void WhenRevised_CarriesBranchNameAndPullRequestUrl()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithBranchName("feat/42-add-feature")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/42")
            .ChecksPending();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please add tests.")];

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.Revise(comments);

        // Assert
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.BranchName.ShouldBe(checksPending.BranchName),
            () => revisionQueued.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl));
    }

    [Fact]
    public void WhenRevised_CarriesReviewComments()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please add tests."),
            new ReviewComment("Missing documentation."),
        ];

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.Revise(comments);

        // Assert
        revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>().Comments.Count.ShouldBe(2);
    }

    [Fact]
    public void WhenRevised_WithOmittedCountAndNewestComment_CarriesThrough()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please add tests.")];
        DateTimeOffset newestCommentAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.Revise(comments, omittedCommentCount: 3, newestCommentAt: newestCommentAt);

        // Assert
        RevisionTrigger.ReviewFeedback rf = revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>();
        rf.ShouldSatisfyAllConditions(
            () => rf.OmittedCommentCount.ShouldBe(3),
            () => rf.NewestConsumedCommentAt.ShouldBe(newestCommentAt));
    }

    [Fact]
    public void WhenRevised_RaisesIssueRevisionQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ChecksPending();
        IReadOnlyList<ReviewComment> comments = [new ReviewComment("Please add tests.")];

        // Act
        checksPending.Revise(comments);

        // Assert
        IssueRevisionQueued domainEvent = checksPending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(checksPending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
