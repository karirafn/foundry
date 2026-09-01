using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.FailedIssueTests;

public sealed class Retry
{
    [Fact]
    public void WhenRetried_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Failed();

        // Act
        FreshQueuedIssue queued = failed.Retry();

        // Assert
        queued.Id.ShouldBe(failed.Id);
    }

    [Fact]
    public void WhenRetried_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Failed();

        // Act
        failed.Retry();

        // Assert
        IssueQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenRetried_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        FailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Failed();

        // Act
        FreshQueuedIssue queued = failed.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(failed.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => queued.Title.ShouldBe(failed.Title),
            () => queued.Body.ShouldBe(failed.Body),
            () => queued.DetectedAt.ShouldBe(failed.DetectedAt));
    }
}
