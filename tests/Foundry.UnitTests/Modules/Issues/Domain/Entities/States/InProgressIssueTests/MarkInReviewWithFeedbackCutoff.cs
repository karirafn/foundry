using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkInReviewWithFeedbackCutoff
{
    [Fact]
    public void WhenMarkedInReviewWithFeedbackCutoff_ReviewIssueHasFeedbackCutoffAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        DateTimeOffset feedbackCutoffAt = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);

        // Act
        ReviewIssue review = inProgress.MarkInReview(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            feedbackCutoffAt);

        // Assert
        review.FeedbackCutoffAt.ShouldBe(feedbackCutoffAt);
    }
}
