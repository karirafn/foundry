using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ReviewIssueTests;

public sealed class Fail
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
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        return inProgress.MarkInReview(Guid.NewGuid(), "foundry/1/add-feature", "https://github.com/owner/repo/pull/5", DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenFailed_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = review.Fail("PR was closed without merge", "pr_closed", failedAt);

        // Assert
        failed.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasWorkerRunIdFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = review.Fail("PR was closed without merge", "pr_closed", failedAt);

        // Assert
        failed.WorkerRunId.ShouldBe(review.WorkerRunId);
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        string failureReason = "PR was closed without merge";

        // Act
        ContinuableFailedIssue failed = review.Fail(failureReason, "pr_closed", failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.BranchName.ShouldBe(review.BranchName),
            () => failed.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }

    [Fact]
    public void WhenFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        review.Fail("PR was closed without merge", "pr_closed", failedAt);

        // Assert
        IssueContinuableFailed domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
