using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class RecordActivity
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New(), WorkerRunId.New());
        return starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From("feat/1-default"),
            MonitoredRepositoryId.New());
    }

    [Fact]
    public void WhenCalled_SetsLastActivityAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordActivity(observedAt);

        // Assert
        active.LastActivityAt.ShouldBe(observedAt);
    }

    [Fact]
    public void WhenCalledWithLaterTime_AdvancesLastActivityAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        active.RecordActivity(first);
        active.ClearDomainEvents();

        // Act
        active.RecordActivity(later);

        // Assert
        active.LastActivityAt.ShouldBe(later);
    }

    [Fact]
    public void WhenCalledWithEarlierTime_DoesNotRegressLastActivityAt()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset earlier = new(2026, 6, 29, 9, 0, 0, TimeSpan.Zero);
        active.RecordActivity(first);
        active.ClearDomainEvents();

        // Act
        active.RecordActivity(earlier);

        // Assert
        active.LastActivityAt.ShouldBe(first);
    }

    [Fact]
    public void WhenCalledWithEarlierTime_DoesNotRaiseDomainEvent()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset earlier = new(2026, 6, 29, 9, 0, 0, TimeSpan.Zero);
        active.RecordActivity(first);
        active.ClearDomainEvents();

        // Act
        active.RecordActivity(earlier);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCalledWithLaterTime_RaisesWorkerActivityObserved()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordActivity(observedAt);

        // Assert
        WorkerActivityObserved domainEvent = active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.LastActivityAt.ShouldBe(observedAt),
            () => domainEvent.NewCommitMarker.ShouldBeNull());
    }
}
