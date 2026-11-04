using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuableFailedIssueTests;

public sealed class RetryFresh
{
    [Fact]
    public void WhenRetriedFresh_ReturnsFreshQueuedIssueWithFreshDispatchContext()
    {
        // Arrange
        ContinuableFailedIssue failed = new IssueBuilder()
            .WithIssueNumber(42)
            .WithBranchName("feat/42-old-attempt")
            .ContinuableFailed();

        // Act
        FreshQueuedIssue queued = failed.RetryFresh();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => queued.MonitoredRepositoryId.ShouldBe(failed.MonitoredRepositoryId),
            () => queued.Context.ShouldBeOfType<DispatchContext.Fresh>());
    }

    [Fact]
    public void WhenRetriedFresh_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed();

        // Act
        failed.RetryFresh();

        // Assert
        IssueQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
