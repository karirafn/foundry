using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuableFailedIssueTests;

public sealed class FromReview
{
    [Fact]
    public void WhenCreatedFromReview_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(review, "PR was closed", "pr_closed", failedAt);

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
    public void WhenCreatedFromReview_CopiesBranchNameAndPullRequestUrlFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();

        // Act
        ContinuableFailedIssue failed = ContinuableFailedIssue.FromReview(review, "PR was closed", "pr_closed", DateTimeOffset.UtcNow);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.BranchName.ShouldBe(review.BranchName),
            () => failed.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }
}
