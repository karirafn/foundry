using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class MarkBlocked
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenTriageNamesBlockers_WaitsOnThemWithTheVerdictRecorded()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().TriageInProgress();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry policy."), new TriageBlocker(31, "Needs the queue.")],
            DecidedAt);

        // Act
        TriageBlockedIssue blocked = triaging.MarkBlocked(verdict, DateTimeOffset.UtcNow);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.TriageVerdict.ShouldBe(verdict),
            () => blocked.BlockedBy.ShouldBe([15, 31]));
    }

    [Fact]
    public void WhenIssueAlreadyHasRecordedBlockers_KeepsThoseNotNamedAfterTheNamedOnes()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder()
            .TriageBlocked([31, 15])
            .Unblock(DateTimeOffset.UtcNow)
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow);
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry policy."), new TriageBlocker(42, "Needs the queue.")],
            DecidedAt);

        // Act
        TriageBlockedIssue blocked = triaging.MarkBlocked(verdict, DateTimeOffset.UtcNow);

        // Assert
        blocked.BlockedBy.ShouldBe([15, 42, 31]);
    }

    [Fact]
    public void WhenTriageNamesBlockers_RaisesTriageBlockedAndTheBlockersNamedByTheRun()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = new IssueBuilder().WithWorkerRunId(workerRunId).TriageInProgress();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry policy.")],
            DecidedAt);

        // Act
        triaging.MarkBlocked(verdict, DateTimeOffset.UtcNow);

        // Assert
        triaging.DomainEvents.OfType<IssueTriageBlocked>().ShouldHaveSingleItem();
        TriageBlockersNamed named = triaging.DomainEvents.OfType<TriageBlockersNamed>().ShouldHaveSingleItem();
        named.ShouldSatisfyAllConditions(
            () => named.IssueId.ShouldBe(triaging.Id),
            () => named.MonitoredRepositoryId.ShouldBe(triaging.MonitoredRepositoryId),
            () => named.IssueNumber.ShouldBe(triaging.IssueNumber),
            () => named.WorkerRunId.ShouldBe(triaging.WorkerRunId),
            () => named.Blockers.ShouldBe(verdict.BlockedBy));
    }
}
