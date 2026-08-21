using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuationQueuedIssueTests;

public sealed class Claim
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

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
        FreshQueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
        return failed.Retry();
    }

    [Fact]
    public void WhenClaimed_ReturnsInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = continuationQueued.Claim(workerRunId);

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.Id.ShouldBe(continuationQueued.Id),
            () => inProgress.WorkerRunId.ShouldBe(workerRunId),
            () => inProgress.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenClaimed_RaisesIssueInProgressDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = CreateContinuationQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        continuationQueued.Claim(workerRunId);

        // Assert
        IssueInProgress domainEvent = continuationQueued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(continuationQueued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
