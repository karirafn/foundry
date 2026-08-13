using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class RecordBranchCommitCount
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
    public void WhenCalledWithNewSha_SetsBranchCommitCount()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordBranchCommitCount(3, "abc1234", observedAt);

        // Assert
        active.BranchCommitCount.ShouldBe(3);
    }

    [Fact]
    public void WhenCalledWithNewSha_SetsLastObservedCommitSha()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordBranchCommitCount(3, "abc1234", observedAt);

        // Assert
        active.LastObservedCommitSha.ShouldBe("abc1234");
    }

    [Fact]
    public void WhenCalledWithNewSha_RaisesWorkerActivityObservedWithCount()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordBranchCommitCount(3, "abc1234", observedAt);

        // Assert
        WorkerActivityObserved domainEvent = active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.LastActivityAt.ShouldBe(observedAt),
            () => domainEvent.CommitCount.ShouldBe(3));
    }

    [Fact]
    public void WhenCalledWithSameSha_UpdatesBranchCommitCount()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        active.RecordBranchCommitCount(3, "abc1234", first);
        active.ClearDomainEvents();

        // Act
        active.RecordBranchCommitCount(4, "abc1234", second);

        // Assert
        active.BranchCommitCount.ShouldBe(4);
    }

    [Fact]
    public void WhenCalledWithSameSha_RaisesNoDomainEvent()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        active.RecordBranchCommitCount(3, "abc1234", first);
        active.ClearDomainEvents();

        // Act
        active.RecordBranchCommitCount(4, "abc1234", second);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCalledWithSmallerCount_StoresSmallerCountVerbatim()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        active.RecordBranchCommitCount(5, "abc1234", first);
        active.ClearDomainEvents();

        // Act — rebase reduces the count
        active.RecordBranchCommitCount(2, "xyz9999", second);

        // Assert
        active.BranchCommitCount.ShouldBe(2);
    }
}
