using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuableFailedIssueTests;

public sealed class Retry
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static ContinuableFailedIssue CreateContinuableFailedIssue(MonitoredRepositoryId repositoryId)
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
        return ContinuableFailedIssue.FromInProgress(
            inProgress,
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenRetried_ReturnsContinuationQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = continuable.Retry();

        // Assert
        continuationQueued.Id.ShouldBe(continuable.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueContinuationQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        continuable.Retry();

        // Assert
        IssueContinuationQueued domainEvent = continuable.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(continuable.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_PreservesBranchNameAndLatestProgress()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = continuable.Retry();

        // Assert
        continuationQueued.ShouldSatisfyAllConditions(
            () => continuationQueued.BranchName.ShouldBe(continuable.BranchName),
            () => continuationQueued.LatestProgress.ShouldBe(continuable.LatestProgress),
            () => continuationQueued.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => continuationQueued.IssueNumber.ShouldBe(continuable.IssueNumber),
            () => continuationQueued.Title.ShouldBe(continuable.Title),
            () => continuationQueued.Body.ShouldBe(continuable.Body),
            () => continuationQueued.DetectedAt.ShouldBe(continuable.DetectedAt));
    }
}
