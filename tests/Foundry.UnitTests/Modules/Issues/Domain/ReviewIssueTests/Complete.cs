using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ReviewIssueTests;

public sealed class Complete
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
        return inProgress.MarkInReview(Guid.NewGuid(), "foundry/1/add-feature", "https://github.com/owner/repo/pull/5");
    }

    [Fact]
    public void WhenCompleted_ReturnsCompletedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = review.Complete(completedAt);

        // Assert
        completed.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenCompleted_RaisesIssueCompletedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        review.Complete(completedAt);

        // Assert
        IssueCompleted domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCompleted_CompletedIssueHasBranchAndPullRequestFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = CreateReviewIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = review.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(review.BranchName),
            () => completed.PullRequestUrl.ShouldBe(review.PullRequestUrl),
            () => completed.CompletedAt.ShouldBe(completedAt),
            () => completed.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
