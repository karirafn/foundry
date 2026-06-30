using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class RecordCommit
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
    public void WhenCalled_AddsMarkerToCommitMarkers()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        CommitMarker marker = CommitMarker.Create(DateTimeOffset.UtcNow, "abc1234", "feat: add something");

        // Act
        active.RecordCommit(marker);

        // Assert
        active.CommitMarkers.ShouldContain(marker);
    }

    [Fact]
    public void WhenCalledWithSameSha_DoesNotAddDuplicate()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        DateTimeOffset first = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 6, 29, 11, 0, 0, TimeSpan.Zero);
        CommitMarker firstMarker = CommitMarker.Create(first, "abc1234", "feat: add something");
        CommitMarker duplicateSha = CommitMarker.Create(second, "abc1234", "feat: add something else");
        active.RecordCommit(firstMarker);
        active.ClearDomainEvents();

        // Act
        active.RecordCommit(duplicateSha);

        // Assert
        active.CommitMarkers.Count.ShouldBe(1);
    }

    [Fact]
    public void WhenCalledWithSameSha_DoesNotRaiseDomainEvent()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        CommitMarker marker = CommitMarker.Create(DateTimeOffset.UtcNow, "abc1234", "feat: add something");
        CommitMarker duplicate = CommitMarker.Create(DateTimeOffset.UtcNow, "abc1234", "feat: different message");
        active.RecordCommit(marker);
        active.ClearDomainEvents();

        // Act
        active.RecordCommit(duplicate);

        // Assert
        active.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void WhenCalledWithDifferentSha_AddsNewMarker()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        CommitMarker first = CommitMarker.Create(DateTimeOffset.UtcNow, "abc1234", "feat: first");
        CommitMarker second = CommitMarker.Create(DateTimeOffset.UtcNow, "xyz9999", "feat: second");
        active.RecordCommit(first);
        active.ClearDomainEvents();

        // Act
        active.RecordCommit(second);

        // Assert
        active.CommitMarkers.Count.ShouldBe(2);
        active.CommitMarkers.ShouldContain(second);
    }

    [Fact]
    public void WhenCalledWithNewSha_RaisesWorkerActivityObservedWithMarker()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ActiveRun active = CreateActiveRun(issueId);
        DateTimeOffset observedAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        CommitMarker marker = CommitMarker.Create(observedAt, "abc1234", "feat: add something");

        // Act
        active.RecordCommit(marker);

        // Assert
        WorkerActivityObserved domainEvent = active.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<WorkerActivityObserved>();

        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(active.Id),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.LastActivityAt.ShouldBe(observedAt),
            () => domainEvent.NewCommitMarker.ShouldBe(marker));
    }
}
