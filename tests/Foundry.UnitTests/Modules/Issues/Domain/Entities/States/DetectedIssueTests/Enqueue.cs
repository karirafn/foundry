using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.DetectedIssueTests;

public sealed class Enqueue
{
    [Fact]
    public void WhenEnqueued_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();

        // Act
        FreshQueuedIssue queued = detected.Enqueue();

        // Assert
        queued.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenEnqueued_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();

        // Act
        detected.Enqueue();

        // Assert
        IssueQueued domainEvent = detected.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(detected.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenEnqueued_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();

        // Act
        FreshQueuedIssue queued = detected.Enqueue();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(detected.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(detected.IssueNumber),
            () => queued.Title.ShouldBe(detected.Title),
            () => queued.Author.ShouldBe(detected.Author),
            () => queued.Url.ShouldBe(detected.Url),
            () => queued.Labels.ShouldBe(detected.Labels),
            () => queued.DetectedAt.ShouldBe(detected.DetectedAt));
    }

    [Fact]
    public void WhenBlockedByIsNotEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Detected();
        detected.SetBlockedBy([5, 10]);

        // Act / Assert
        Should.Throw<InvalidOperationException>(() => detected.Enqueue());
    }
}
