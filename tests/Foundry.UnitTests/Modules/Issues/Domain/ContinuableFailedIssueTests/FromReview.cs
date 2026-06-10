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
    public void WhenCreatedFromReview_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(review, "PR was closed", failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.Id.ShouldBe(review.Id),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.IssueNumber.ShouldBe(review.IssueNumber),
            () => failed.Title.ShouldBe(review.Title),
            () => failed.Body.ShouldBe(review.Body),
            () => failed.Author.ShouldBe(review.Author),
            () => failed.DetectedAt.ShouldBe(review.DetectedAt));
    }

    [Fact]
    public void WhenCreatedFromReview_SetsDefaultLatestProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(review, "PR was closed", DateTimeOffset.UtcNow);

        // Assert
        failed.LatestProgress.ShouldBe("PR was opened and reviewed");
    }

    [Fact]
    public void WhenCreatedFromReview_CopiesBranchNameAndPullRequestUrlFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);

        // Act
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(review, "PR was closed", DateTimeOffset.UtcNow);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.BranchName.ShouldBe(review.BranchName),
            () => failed.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }
}
