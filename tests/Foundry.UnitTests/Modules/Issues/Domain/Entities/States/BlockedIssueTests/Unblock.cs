using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.BlockedIssueTests;

public sealed class Unblock
{
    [Fact]
    public void WhenUnblocked_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected().Block([5]);

        // Act
        FreshQueuedIssue queued = blocked.Unblock();

        // Assert
        queued.Id.ShouldBe(blocked.Id);
    }

    [Fact]
    public void WhenUnblocked_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected().Block([5]);

        // Act
        blocked.Unblock();

        // Assert
        IssueQueued domainEvent = blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(blocked.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenUnblocked_ResultingQueuedIssueHasEmptyBlockedBy()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        BlockedIssue blocked = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected().Block([5]);

        // Act
        FreshQueuedIssue queued = blocked.Unblock();

        // Assert
        queued.BlockedBy.ShouldBeEmpty();
    }
}
