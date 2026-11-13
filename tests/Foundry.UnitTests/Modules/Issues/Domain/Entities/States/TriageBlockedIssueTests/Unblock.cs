using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageBlockedIssueTests;

public sealed class Unblock
{
    [Fact]
    public void WhenUnblocked_GoesToTriageWithTheSameId()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlocked([5]);

        // Act
        TriageQueuedIssue queued = blocked.Unblock(DateTimeOffset.UtcNow);

        // Assert
        queued.Id.ShouldBe(blocked.Id);
    }

    [Fact]
    public void WhenUnblocked_CarriesTheSameBlockedBy()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlocked([5, 8]);

        // Act
        TriageQueuedIssue queued = blocked.Unblock(DateTimeOffset.UtcNow);

        // Assert
        queued.BlockedBy.ShouldBe([5, 8]);
    }

    [Fact]
    public void WhenUnblocked_RaisesIssueTriageQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageBlockedIssue blocked = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).TriageBlocked([5]);

        // Act
        blocked.Unblock(DateTimeOffset.UtcNow);

        // Assert
        IssueTriageQueued domainEvent = blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(blocked.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenUnblocked_RecordsNoTriageVerdict()
    {
        // Arrange
        TriageBlockedIssue blocked = new IssueBuilder().TriageBlocked([5]);

        // Act
        TriageQueuedIssue queued = blocked.Unblock(DateTimeOffset.UtcNow);

        // Assert
        queued.TriageVerdict.ShouldBeNull();
    }
}
