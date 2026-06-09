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
    public void WhenFailed_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = review.Fail("PR was closed without merge", failedAt);

        // Assert
        continuable.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasBranchNameAndPullRequestUrlFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = review.Fail("PR was closed without merge", failedAt);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.BranchName.ShouldBe(review.BranchName),
            () => continuable.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasCorrectFailureReasonAndFailedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        string failureReason = "PR was closed without merge";

        // Act
        ContinuableFailedIssue continuable = review.Fail(failureReason, failedAt);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.FailureReason.ShouldBe(failureReason),
            () => continuable.FailedAt.ShouldBe(failedAt),
            () => continuable.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenFailed_LatestProgressDefaultsToPrOpenedAndReviewed()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = review.Fail("PR was closed without merge", failedAt);

        // Assert
        continuable.LatestProgress.ShouldBe("PR was opened and reviewed");
    }

    [Fact]
    public void WhenFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        review.Fail("PR was closed without merge", failedAt);

        // Assert
        IssueContinuableFailed domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
