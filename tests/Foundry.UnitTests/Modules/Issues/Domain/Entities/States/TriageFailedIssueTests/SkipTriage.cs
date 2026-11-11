using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageFailedIssueTests;

public sealed class SkipTriage
{
    [Fact]
    public void WhenItHasNoOpenBlockers_QueuesItWithoutAVerdictAndItsKindUnchanged()
    {
        // Arrange
        TriageFailedIssue failed = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .TriageFailed();

        // Act
        Issue next = failed.SkipTriage();

        // Assert
        FreshQueuedIssue queued = next.ShouldBeOfType<FreshQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.TriageVerdict.ShouldBeNull(),
            () => queued.KindClassification.ShouldBe(KindClassification.Unclassified),
            () => queued.DispatchBranchName.ShouldNotBeNull().Value.ShouldStartWith("feat/"),
            () => failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>());
    }

    [Fact]
    public void WhenItHasOpenBlockers_BlocksIt()
    {
        // Arrange
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();
        queued.SetBlockedBy([9]);
        TriageFailedIssue failed = queued.Claim(WorkerRunId.New())
            .MarkFailed("boom", FailureCategory.NonZeroExit, DateTimeOffset.UnixEpoch);

        // Act
        Issue next = failed.SkipTriage();

        // Assert
        next.ShouldBeOfType<BlockedIssue>().BlockedBy.ShouldBe([9]);
    }
}
