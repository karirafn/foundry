using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuationQueuedIssueTests;

public sealed class Claim
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static ContinuationQueuedIssue CreateContinuationQueuedIssue(MonitoredRepositoryId repositoryId)
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
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromInProgress(
            inProgress,
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            DateTimeOffset.UtcNow);
        return continuable.Retry();
    }

    [Fact]
    public void WhenClaimed_ReturnsInProgressIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = continuationQueued.Claim(workerRunId);

        // Assert
        inProgress.Id.ShouldBe(continuationQueued.Id);
    }

    [Fact]
    public void WhenClaimed_RaisesIssueInProgressDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);

        // Act
        continuationQueued.Claim(Guid.NewGuid());

        // Assert
        IssueInProgress domainEvent = continuationQueued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(continuationQueued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenClaimed_InProgressIssueHasProvidedWorkerRunId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = continuationQueued.Claim(workerRunId);

        // Assert
        inProgress.WorkerRunId.ShouldBe(workerRunId);
    }

    [Fact]
    public void WhenClaimed_SharedPropertiesAreCopied()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);

        // Act
        InProgressIssue inProgress = continuationQueued.Claim(Guid.NewGuid());

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => inProgress.IssueNumber.ShouldBe(continuationQueued.IssueNumber),
            () => inProgress.Title.ShouldBe(continuationQueued.Title),
            () => inProgress.Body.ShouldBe(continuationQueued.Body),
            () => inProgress.DetectedAt.ShouldBe(continuationQueued.DetectedAt));
    }
}
