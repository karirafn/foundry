using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.UnchangedIssueTests;

public sealed class Retry
{
    [Fact]
    public void WhenRetried_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Unchanged();

        // Act
        FreshQueuedIssue queued = unchanged.Retry();

        // Assert
        queued.Id.ShouldBe(unchanged.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Unchanged();

        // Act
        unchanged.Retry();

        // Assert
        IssueQueued domainEvent = unchanged.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(unchanged.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Unchanged();

        // Act
        FreshQueuedIssue queued = unchanged.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(unchanged.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(unchanged.IssueNumber),
            () => queued.Title.ShouldBe(unchanged.Title),
            () => queued.DetectedAt.ShouldBe(unchanged.DetectedAt));
    }
}
