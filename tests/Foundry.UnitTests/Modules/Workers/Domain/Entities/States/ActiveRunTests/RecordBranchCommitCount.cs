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
    public void WhenCalledWithSameSha_LeavesCountAtFirstValue()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        active.RecordBranchCommitCount(3, "abc1234", first);
        active.ClearDomainEvents();

        // Act — same SHA, count argument differs (does not occur in production, but guard must hold)
        active.RecordBranchCommitCount(4, "abc1234", second);

        // Assert — count unchanged because the SHA dedup guard fires before mutation
        active.BranchCommitCount.ShouldBe(3);
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
    public void WhenCalledWithNullShaTwice_RaisesNoDomainEventOnSecondCall()
    {
        // Arrange — first call: null SHA, raises event (null != initial null? No — initial LastObservedCommitSha is null).
        // So the first null call is also a dedup-no-op. We need a prior non-null SHA to show
        // the null→null dedup path: set a sha, clear events, then call with null (first transition),
        // clear events, then call with null again (second call — same sha).
        ActiveRun active = CreateActiveRun();
        DateTimeOffset t1 = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        DateTimeOffset t3 = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        // Establish a non-null SHA first
        active.RecordBranchCommitCount(3, "abc1234", t1);
        active.ClearDomainEvents();

        // Transition to null (branch gone / NotFound) — event should be raised
        active.RecordBranchCommitCount(0, null, t2);
        active.ClearDomainEvents();

        // Act — second call with null SHA (same as current LastObservedCommitSha)
        active.RecordBranchCommitCount(0, null, t3);

        // Assert — no event raised; count unchanged
        active.DomainEvents.ShouldBeEmpty();
        active.BranchCommitCount.ShouldBe(0);
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
