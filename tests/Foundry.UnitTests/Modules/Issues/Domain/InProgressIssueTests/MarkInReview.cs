using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.InProgressIssueTests;

public sealed class MarkInReview
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static InProgressIssue CreateInProgressIssue(MonitoredRepositoryId repositoryId)
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
        return queued.Claim(Guid.NewGuid());
    }

    [Fact]
    public void WhenMarkedInReview_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        ReviewIssue review = inProgress.MarkInReview(
            workerRunId,
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);

        // Assert
        review.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedInReview_RaisesIssueInReviewDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        inProgress.MarkInReview(
            workerRunId,
            "foundry/1/add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);

        // Assert
        IssueInReview domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInReview>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedInReview_ReviewIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string branchName = "foundry/1/add-feature";
        string pullRequestUrl = "https://github.com/owner/repo/pull/5";

        // Act
        ReviewIssue review = inProgress.MarkInReview(workerRunId, branchName, pullRequestUrl, DateTimeOffset.UtcNow);

        // Assert
        review.ShouldSatisfyAllConditions(
            () => review.WorkerRunId.ShouldBe(workerRunId),
            () => review.BranchName.ShouldBe(branchName),
            () => review.PullRequestUrl.ShouldBe(pullRequestUrl),
            () => review.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => review.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => review.Title.ShouldBe(inProgress.Title),
            () => review.Body.ShouldBe(inProgress.Body),
            () => review.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
