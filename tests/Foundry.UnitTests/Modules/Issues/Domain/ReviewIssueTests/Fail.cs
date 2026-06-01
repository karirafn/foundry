using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ReviewIssueTests;

public sealed class Fail
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

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
    public void WhenFailed_ReturnsFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = review.Fail("PR was closed without merge", failedAt);

        // Assert
        failed.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenFailed_FailedIssueHasWorkerRunIdFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = review.Fail("PR was closed without merge", failedAt);

        // Assert
        failed.WorkerRunId.ShouldBe(review.WorkerRunId);
    }

    [Fact]
    public void WhenFailed_FailedIssueHasCorrectFailureReasonAndFailedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        string failureReason = "PR was closed without merge";

        // Act
        FailedIssue failed = review.Fail(failureReason, failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenFailed_RaisesIssueFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        review.Fail("PR was closed without merge", failedAt);

        // Assert
        IssueFailed domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
