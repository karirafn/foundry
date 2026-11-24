using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuationBlockedIssueTests;

public sealed class Unblock
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenBlocked_HoldsTheContinuationWithItsBlockers()
    {
        // Arrange
        ContinuationQueuedIssue queued = new IssueBuilder().WithBranchName("feat/42-x").ContinuationQueued();

        // Act
        ContinuationBlockedIssue blocked = queued.Block([12], Now);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.BlockedBy.ShouldBe([12]),
            () => blocked.WaitsOnBlockers.ShouldBeTrue(),
            () => blocked.BranchName.ShouldBe("feat/42-x"));
        queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationBlocked>();
    }

    [Fact]
    public void WhenUnblocked_RequeuesTheSameContinuationOnTheSameBranch()
    {
        // Arrange
        ContinuationQueuedIssue queued = new IssueBuilder()
            .WithBranchName("feat/42-x")
            .WithFailureReason("Container OOM")
            .WithFailureCategory(FailureCategory.NonZeroExit)
            .ContinuationQueued();
        ContinuationBlockedIssue blocked = queued.Block([12], Now);

        // Act
        ContinuationQueuedIssue requeued = blocked.Unblock(Now);

        // Assert
        requeued.ShouldSatisfyAllConditions(
            () => requeued.Id.ShouldBe(queued.Id),
            () => requeued.BranchName.ShouldBe("feat/42-x"),
            () => requeued.FailureReason.ShouldBe("Container OOM"),
            () => requeued.Context.ShouldBe(queued.Context));
    }

    [Fact]
    public void WhenUnblocked_RaisesIssueContinuationQueued()
    {
        // Arrange
        ContinuationBlockedIssue blocked = new IssueBuilder().ContinuationBlocked([12]);

        // Act
        blocked.Unblock(Now);

        // Assert
        blocked.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>().IssueId.ShouldBe(blocked.Id);
    }
}
