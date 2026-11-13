using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageQueuedIssueTests;

public sealed class Claim
{
    [Fact]
    public void WhenClaimed_GoesToTriageInProgressWithTheRunAndBlockers()
    {
        // Arrange
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();
        queued.SetBlockedBy([7]);
        WorkerRunId workerRunId = WorkerRunId.New();

        // Act
        TriageInProgressIssue inProgress = queued.Claim(workerRunId, DateTimeOffset.UtcNow);

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.Id.ShouldBe(queued.Id),
            () => inProgress.WorkerRunId.ShouldBe(workerRunId),
            () => inProgress.BlockedBy.ShouldBe([7]),
            () => queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageInProgress>());
    }
}
