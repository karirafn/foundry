using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuationQueuedIssueTests;

public sealed class Claim
{
    [Fact]
    public void WhenClaimed_ReturnsInProgressIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuationQueuedIssue continuationQueued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed().Retry();
        WorkerRunId workerRunId = WorkerRunId.New();

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
        ContinuationQueuedIssue continuationQueued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed().Retry();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        continuationQueued.Claim(workerRunId);

        // Assert
        IssueInProgress domainEvent = continuationQueued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(continuationQueued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
