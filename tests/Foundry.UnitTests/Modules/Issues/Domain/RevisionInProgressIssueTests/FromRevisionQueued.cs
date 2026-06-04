using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.RevisionInProgressIssueTests;

public sealed class FromRevisionQueued
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static RevisionQueuedIssue CreateRevisionQueuedIssue(MonitoredRepositoryId repositoryId)
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
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
        return review.Revise([new ReviewComment("Please fix the formatting.")]);
    }

    [Fact]
    public void WhenClaimed_ReturnsRevisionInProgressIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);
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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);
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
        RevisionQueuedIssue revisionQueued = CreateRevisionQueuedIssue(repositoryId);

        // Act
        revisionQueued.Claim(Guid.NewGuid());

        // Assert
        IssueRevisionInProgress domainEvent = revisionQueued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionQueued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
