using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.DetectedIssueTests;

public sealed class QueueTriage
{
    [Fact]
    public void WhenQueued_GoesToTriageWithItsSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(42)
            .WithTitle("Add retry")
            .WithKindClassification(new KindClassification.Labelled(IssueKind.Bug))
            .Detected();

        // Act
        TriageQueuedIssue queued = detected.QueueTriage();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(detected.Id),
            () => queued.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => queued.IssueNumber.ShouldBe(42),
            () => queued.Title.ShouldBe("Add retry"),
            () => queued.KindClassification.ShouldBe(new KindClassification.Labelled(IssueKind.Bug)),
            () => queued.TriageVerdict.ShouldBeNull());
    }

    [Fact]
    public void WhenQueued_RaisesIssueTriageQueued()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();

        // Act
        detected.QueueTriage();

        // Assert
        IssueTriageQueued domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenItHasOpenBlockers_StillGoesToTriageCarryingThem()
    {
        // Arrange
        DetectedIssue detected = new IssueBuilder().Detected();
        detected.SetBlockedBy([3, 5]);

        // Act
        TriageQueuedIssue queued = detected.QueueTriage();

        // Assert
        queued.BlockedBy.ShouldBe([3, 5]);
    }
}
