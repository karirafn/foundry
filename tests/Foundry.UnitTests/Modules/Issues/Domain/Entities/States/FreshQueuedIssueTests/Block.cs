using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FreshQueuedIssueTests;

public sealed class Block
{
    [Fact]
    public void WhenBlocked_ReturnsBlockedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        IReadOnlyList<int> blockers = [10];

        // Act
        BlockedIssue blocked = queued.Block(blockers);

        // Assert
        blocked.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenBlocked_RaisesIssueBlockedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        IReadOnlyList<int> blockers = [10];

        // Act
        queued.Block(blockers);

        // Assert
        IssueBlocked domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(queued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlocked_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        IReadOnlyList<int> blockers = [10, 20];

        // Act
        BlockedIssue blocked = queued.Block(blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }

    [Fact]
    public void WhenBlockersIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FreshQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).FreshQueued();
        IReadOnlyList<int> blockers = [];

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => queued.Block(blockers));
    }
}
