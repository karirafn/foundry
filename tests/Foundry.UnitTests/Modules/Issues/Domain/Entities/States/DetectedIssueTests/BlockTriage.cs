using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.DetectedIssueTests;

public sealed class BlockTriage
{
    [Fact]
    public void WhenItHasOpenBlockers_WaitsUntriagedCarryingThem()
    {
        // Arrange
        DetectedIssue detected = new IssueBuilder().WithIssueNumber(42).Detected();

        // Act
        TriageBlockedIssue blocked = detected.BlockTriage([3, 5], DateTimeOffset.UtcNow);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.Id.ShouldBe(detected.Id),
            () => blocked.IssueNumber.ShouldBe(42),
            () => blocked.BlockedBy.ShouldBe([3, 5]),
            () => blocked.TriageVerdict.ShouldBeNull());
    }

    [Fact]
    public void WhenBlocked_RaisesIssueTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();

        // Act
        detected.BlockTriage([3], DateTimeOffset.UtcNow);

        // Assert
        IssueTriageBlocked domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenGivenNoBlockers_Throws()
    {
        // Arrange
        DetectedIssue detected = new IssueBuilder().Detected();

        // Act
        Action act = () => detected.BlockTriage([], DateTimeOffset.UtcNow);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
    }
}
