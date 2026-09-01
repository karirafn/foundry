using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.DetectedIssueTests;

public sealed class Block
{
    [Fact]
    public void WhenBlocked_ReturnsBlockedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        IReadOnlyList<int> blockers = [10];

        // Act
        BlockedIssue blocked = detected.Block(blockers);

        // Assert
        blocked.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenBlocked_RaisesIssueBlockedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        IReadOnlyList<int> blockers = [10];

        // Act
        detected.Block(blockers);

        // Assert
        IssueBlocked domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlocked_BlockedByContainsSuppliedBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        IReadOnlyList<int> blockers = [10, 20];

        // Act
        BlockedIssue blocked = detected.Block(blockers);

        // Assert
        blocked.BlockedBy.ShouldBe(blockers);
    }

    [Fact]
    public void WhenBlockersIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        IReadOnlyList<int> blockers = [];

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => detected.Block(blockers));
    }
}
