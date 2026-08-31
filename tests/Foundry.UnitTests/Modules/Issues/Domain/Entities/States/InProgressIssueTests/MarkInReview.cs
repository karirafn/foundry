using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkInReview
{
    [Fact]
    public void WhenMarkedInReview_ReturnsReviewIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
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
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
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
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
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
