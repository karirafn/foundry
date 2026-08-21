using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionFailedIssueTests;

public sealed class Retry
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static RevisionFailedIssue CreateRevisionFailedIssue(MonitoredRepositoryId repositoryId)
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
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
        IReadOnlyList<ReviewComment> comments =
        [
            new ReviewComment("Please fix the formatting."),
            new ReviewComment("Rename this variable.", "src/Foo.cs", 42),
        ];
        RevisionQueuedIssue revisionQueued = review.Revise(comments);
        RevisionInProgressIssue revisionInProgress = revisionQueued.Claim(Guid.NewGuid());
        return revisionInProgress.MarkFailed(
            Guid.NewGuid(),
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenRetried_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionFailedIssue failed = CreateRevisionFailedIssue(repositoryId);

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
        RevisionFailedIssue failed = CreateRevisionFailedIssue(repositoryId);

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
        RevisionFailedIssue failed = CreateRevisionFailedIssue(repositoryId);

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
            () => revisionQueued.Body.ShouldBe(failed.Body),
            () => revisionQueued.DetectedAt.ShouldBe(failed.DetectedAt));
    }
}
