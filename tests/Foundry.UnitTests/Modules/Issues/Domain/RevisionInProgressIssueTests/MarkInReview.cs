using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.RevisionInProgressIssueTests;

public sealed class MarkInReview
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static RevisionInProgressIssue CreateRevisionInProgressIssue(MonitoredRepositoryId repositoryId)
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
        RevisionQueuedIssue revisionQueued = review.Revise([new ReviewComment("Please fix.")]);
        return revisionQueued.Claim(Guid.NewGuid());
    }

    [Fact]
    public void WhenMarkedInReview_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = CreateRevisionInProgressIssue(repositoryId);
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        review.Id.ShouldBe(revisionInProgress.Id);
    }

    [Fact]
    public void WhenMarkedInReview_RaisesIssueInReviewDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = CreateRevisionInProgressIssue(repositoryId);
        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow;

        // Act
        revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        IssueInReview domainEvent = revisionInProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(revisionInProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedInReview_ReviewIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        RevisionInProgressIssue revisionInProgress = CreateRevisionInProgressIssue(repositoryId);
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        ReviewIssue review = revisionInProgress.MarkInReview(feedbackCutoffAt);

        // Assert
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(revisionInProgress.WorkerRunId),
            () => review.BranchName.ShouldBe(revisionInProgress.BranchName),
            () => review.PullRequestUrl.ShouldBe(revisionInProgress.PullRequestUrl),
            () => review.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => review.IssueNumber.ShouldBe(revisionInProgress.IssueNumber),
            () => review.Title.ShouldBe(revisionInProgress.Title),
            () => review.Body.ShouldBe(revisionInProgress.Body),
            () => review.DetectedAt.ShouldBe(revisionInProgress.DetectedAt));
    }
}
