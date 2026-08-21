using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class Revise
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static ReviewIssue CreateReviewIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);
        FreshQueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        return inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenRevised_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
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
        ReviewIssue review = CreateReviewIssue(repositoryId);
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
        ReviewIssue review = CreateReviewIssue(repositoryId);
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
}
