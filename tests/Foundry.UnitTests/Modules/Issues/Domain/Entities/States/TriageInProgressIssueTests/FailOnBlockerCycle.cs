using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class FailOnBlockerCycle
{
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenALinkWouldCloseACycle_FailsTriageNamingTheCycleAndTheBlockerToRemove()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageInProgressIssue triaging = new IssueBuilder()
            .WithIssueNumber(42)
            .WithWorkerRunId(workerRunId)
            .TriageInProgress();
        DependencyCycle cycle = DependencyCycle.Create([42, 15, 31, 42])
            .ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value;

        // Act
        TriageFailedIssue failed = triaging.FailOnBlockerCycle(cycle, FailedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureCategory.ShouldBe(FailureCategory.BlockerCycle),
            () => failed.FailureReason.ShouldBe(
                "Linking #15 as a blocker would close a dependency cycle: #42 → #15 → #31 → #42, each blocked by "
                + "the next. Remove one of these links, then retry triage."),
            () => failed.FailedAt.ShouldBe(FailedAt),
            () => failed.WorkerRunId.ShouldBe(workerRunId));
    }

    [Fact]
    public void WhenALinkWouldCloseACycle_RaisesTriageFailedAndNamesNoBlockers()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().WithIssueNumber(42).TriageInProgress();
        DependencyCycle cycle = DependencyCycle.Create([42, 15, 31, 42])
            .ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value;

        // Act
        triaging.FailOnBlockerCycle(cycle, FailedAt);

        // Assert
        triaging.DomainEvents.ShouldSatisfyAllConditions(
            () => triaging.DomainEvents.OfType<IssueTriageFailed>().ShouldHaveSingleItem(),
            () => triaging.DomainEvents.OfType<TriageBlockersNamed>().ShouldBeEmpty());
    }
}
