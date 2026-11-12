using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageQueuedIssueTests;

public sealed class Block
{
    [Fact]
    public void WhenItHasOpenBlockers_WaitsUntriagedCarryingThem()
    {
        // Arrange
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();

        // Act
        TriageBlockedIssue blocked = queued.Block([7]);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.Id.ShouldBe(queued.Id),
            () => blocked.BlockedBy.ShouldBe([7]));
    }

    [Fact]
    public void WhenBlocked_RaisesIssueTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageQueuedIssue queued = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).TriageQueued();

        // Act
        queued.Block([7]);

        // Assert
        IssueTriageBlocked domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(queued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenGivenNoBlockers_Throws()
    {
        // Arrange
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();

        // Act
        Action act = () => queued.Block([]);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
    }
}
