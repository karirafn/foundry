using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class MarkFailed
{
    [Fact]
    public void WhenTheRunFails_GoesToTriageFailedWithTheReason()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset failedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        TriageInProgressIssue triaging = new IssueBuilder().WithWorkerRunId(workerRunId).TriageInProgress();

        // Act
        TriageFailedIssue failed = triaging.MarkFailed("Verdict missing from output", FailureCategory.VerdictInvalid, failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(workerRunId),
            () => failed.FailureReason.ShouldBe("Verdict missing from output"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.VerdictInvalid),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.IsRestingState().ShouldBeTrue(),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageFailed>());
    }
}
