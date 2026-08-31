using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuableFailedIssueTests;

public sealed class Retry
{
    [Fact]
    public void WhenRetried_ReturnsContinuationQueuedIssueWithBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed();

        // Act
        ContinuationQueuedIssue queued = failed.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.BranchName.ShouldBe(failed.BranchName));
    }

    [Fact]
    public void WhenRetried_RaisesIssueContinuationQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed();

        // Act
        failed.Retry();

        // Assert
        IssueContinuationQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
