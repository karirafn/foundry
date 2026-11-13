using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageFailedIssueTests;

public sealed class RetryTriage
{
    [Fact]
    public void WhenRetried_GoesBackToTriage()
    {
        // Arrange
        TriageFailedIssue failed = new IssueBuilder().TriageFailed();

        // Act
        TriageQueuedIssue queued = failed.RetryTriage(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueTriageQueued>());
    }

    [Fact]
    public void WhenTheFailureWasARejectedBlockerLink_GoesBackToTriageCarryingTheBlockers()
    {
        // Arrange
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs the retry policy.")],
            DateTimeOffset.UnixEpoch);
        TriageFailedIssue failed = new IssueBuilder()
            .TriageBlockedFromTriage(verdict)
            .FailBlockerLink(WorkerRunId.New(), 15, "issue is locked", DateTimeOffset.UnixEpoch);

        // Act
        TriageQueuedIssue queued = failed.RetryTriage(DateTimeOffset.UtcNow);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.BlockedBy.ShouldBe([15]));
    }
}
