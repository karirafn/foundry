using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuableFailedIssueTests;

public sealed class FromReview
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
        return inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenCreatedFromReview_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        string failureReason = "PR was closed without merge";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            failureReason,
            failedAt);

        // Assert
        continuable.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenCreatedFromReview_HasBranchNameAndPullRequestUrlFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        string failureReason = "PR was closed without merge";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            failureReason,
            failedAt);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.BranchName.ShouldBe(review.BranchName),
            () => continuable.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }

    [Fact]
    public void WhenCreatedFromReview_LatestProgressDefaultsToPrOpenedAndReviewed()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        string failureReason = "PR was closed without merge";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            failureReason,
            failedAt);

        // Assert
        continuable.LatestProgress.ShouldBe("PR was opened and reviewed");
    }

    [Fact]
    public void WhenCreatedFromReview_HasCorrectFailureReasonAndFailedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        string failureReason = "PR was closed without merge";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            failureReason,
            failedAt);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.FailureReason.ShouldBe(failureReason),
            () => continuable.FailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenCreatedFromReview_WorkerRunIdCopiedFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            "PR was closed without merge",
            DateTimeOffset.UtcNow);

        // Assert
        continuable.WorkerRunId.ShouldBe(review.WorkerRunId);
    }

    [Fact]
    public void WhenCreatedFromReview_CopiesSharedPropertiesFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromReview(
            review,
            "PR was closed without merge",
            DateTimeOffset.UtcNow);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.MonitoredRepositoryId.ShouldBe(review.MonitoredRepositoryId),
            () => continuable.IssueNumber.ShouldBe(review.IssueNumber),
            () => continuable.Title.ShouldBe(review.Title),
            () => continuable.Body.ShouldBe(review.Body),
            () => continuable.DetectedAt.ShouldBe(review.DetectedAt));
    }
}
