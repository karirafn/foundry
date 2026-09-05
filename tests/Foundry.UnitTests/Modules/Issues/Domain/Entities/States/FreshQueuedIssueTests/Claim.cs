using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FreshQueuedIssueTests;

public sealed class Claim
{
    [Fact]
    public void WhenClaimed_ReturnsInProgressIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        InProgressIssue inProgress = queued.Claim(workerRunId);

        // Assert
        inProgress.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenClaimed_RaisesIssueInProgressDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();

        // Act
        queued.Claim(WorkerRunId.New());

        // Assert
        IssueInProgress domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(queued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenClaimed_SharedPropertiesAreCopied()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();

        // Act
        InProgressIssue inProgress = queued.Claim(WorkerRunId.New());

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => inProgress.IssueNumber.ShouldBe(1),
            () => inProgress.Title.ShouldBe("Test Issue"),
            () => inProgress.Labels.ShouldBe(["foundry"]));
    }

    [Fact]
    public void WhenClaimed_WorkerRunIdMatchesProvidedId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        InProgressIssue inProgress = queued.Claim(workerRunId);

        // Assert
        inProgress.WorkerRunId.ShouldBe(workerRunId);
    }
}
