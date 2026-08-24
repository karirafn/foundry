using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.ActiveRunTests;

public sealed class RecordBranchCommitCount
{
    [Fact]
    public void WhenCalledWithNewSha_SetsBranchCommitCount()
    {
        // Arrange
        ActiveRun active = new ActiveRunBuilder().Build();
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
        ActiveRun active = new ActiveRunBuilder().Build();
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
        ActiveRun active = new ActiveRunBuilder()
            .WithIssueId(issueId)
            .Build();
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
    public void WhenFirstObservationHasNullSha_PersistsCountAndRaisesEvent()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = new ActiveRunBuilder()
            .WithIssueId(issueId)
            .Build();
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act — first call: count non-zero, SHA null (GitHub run not yet resolved)
        active.RecordBranchCommitCount(5, null, observedAt);

        // Assert
        WorkerActivityObserved domainEvent = active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        active.ShouldSatisfyAllConditions(
            () => active.BranchCommitCount.ShouldBe(5),
            () => domainEvent.CommitCount.ShouldBe(5));
    }

    [Fact]
    public void WhenCountAndShaMatchPersisted_RaisesNoEventAndMutatesNothing()
    {
        // Arrange
        ActiveRun active = new ActiveRunBuilder()
            .WithObservedCommit(3, "abc1234");
        DateTimeOffset observedAt = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);

        // Act — same count and same SHA
        active.RecordBranchCommitCount(3, "abc1234", observedAt);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
        active.BranchCommitCount.ShouldBe(3);
        active.LastObservedCommitSha.ShouldBe("abc1234");
    }

    [Fact]
    public void WhenCountChangesButShaIsUnchanged_PersistsNewCountAndRaisesEvent()
    {
        // Arrange — simulate a scenario where SHA stays the same but commit count advances
        ActiveRun active = new ActiveRunBuilder()
            .WithObservedCommit(3, "abc1234");
        DateTimeOffset observedAt = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);

        // Act
        active.RecordBranchCommitCount(4, "abc1234", observedAt);

        // Assert
        active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        active.BranchCommitCount.ShouldBe(4);
    }

    [Fact]
    public void WhenNotFoundResetAfterNonZeroCount_ResetsCountToZeroAndRaisesEvent()
    {
        // Arrange — prior successful observation
        ActiveRun active = new ActiveRunBuilder()
            .WithObservedCommit(3, "abc1234");
        DateTimeOffset observedAt = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);

        // Act — NotFound-style reset: count=0, SHA=null
        active.RecordBranchCommitCount(0, null, observedAt);

        // Assert
        active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        active.BranchCommitCount.ShouldBe(0);
    }

    [Fact]
    public void WhenCalledWithNullShaTwice_RaisesNoDomainEventOnSecondCall()
    {
        // Arrange — establish count=3, sha="abc1234", then reset to (0, null)
        ActiveRun active = new ActiveRunBuilder()
            .WithObservedCommit(3, "abc1234");
        DateTimeOffset t2 = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        DateTimeOffset t3 = new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        // Transition to (0, null) — raises event; clear it
        active.RecordBranchCommitCount(0, null, t2);
        active.ClearDomainEvents();

        // Act — second call with null SHA and count=0 (both match persisted)
        active.RecordBranchCommitCount(0, null, t3);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
        active.BranchCommitCount.ShouldBe(0);
    }

    [Fact]
    public void WhenCalledWithSmallerCount_StoresSmallerCountVerbatim()
    {
        // Arrange
        ActiveRun active = new ActiveRunBuilder()
            .WithObservedCommit(5, "abc1234");
        DateTimeOffset observedAt = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);

        // Act — rebase reduces the count
        active.RecordBranchCommitCount(2, "xyz9999", observedAt);

        // Assert
        active.BranchCommitCount.ShouldBe(2);
    }
}
