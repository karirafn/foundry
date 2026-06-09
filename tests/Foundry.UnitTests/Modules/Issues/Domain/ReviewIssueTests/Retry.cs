using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ReviewIssueTests;

public sealed class Retry
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
    public void WhenRetried_ReturnsContinuationQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = review.Retry();

        // Assert
        continuationQueued.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueContinuationQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        review.Retry();

        // Assert
        IssueContinuationQueued domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_ContinuationQueuedIssueHasBranchNameFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = review.Retry();

        // Assert
        continuationQueued.BranchName.ShouldBe(review.BranchName);
    }

    [Fact]
    public void WhenRetried_LatestProgressDefaultsToPrOpenedAndReviewed()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = review.Retry();

        // Assert
        continuationQueued.LatestProgress.ShouldBe("PR was opened and reviewed");
    }

    [Fact]
    public void WhenRetried_ContinuationQueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = review.Retry();

        // Assert
        continuationQueued.ShouldSatisfyAllConditions(
            () => continuationQueued.MonitoredRepositoryId.ShouldBe(review.MonitoredRepositoryId),
            () => continuationQueued.IssueNumber.ShouldBe(review.IssueNumber),
            () => continuationQueued.Title.ShouldBe(review.Title),
            () => continuationQueued.Body.ShouldBe(review.Body),
            () => continuationQueued.DetectedAt.ShouldBe(review.DetectedAt));
    }
}
