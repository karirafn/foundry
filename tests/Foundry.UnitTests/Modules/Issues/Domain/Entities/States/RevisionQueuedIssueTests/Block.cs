using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.RevisionQueuedIssueTests;

public sealed class Block
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenBlocked_HoldsTheRevisionWithItsBlockers()
    {
        // Arrange
        RevisionQueuedIssue queued = new IssueBuilder()
            .WithBranchName("feat/42-x")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/42")
            .RevisionQueued();

        // Act
        RevisionBlockedIssue blocked = queued.Block([12, 15], Now);

        // Assert
        blocked.ShouldSatisfyAllConditions(
            () => blocked.Id.ShouldBe(queued.Id),
            () => blocked.BlockedBy.ShouldBe([12, 15]),
            () => blocked.WaitsOnBlockers.ShouldBeTrue(),
            () => blocked.BranchName.ShouldBe(queued.BranchName),
            () => blocked.LinkedPullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => blocked.Trigger.ShouldBe(queued.Trigger));
    }

    [Fact]
    public void WhenBlocked_RaisesIssueRevisionBlocked()
    {
        // Arrange
        RevisionQueuedIssue queued = new IssueBuilder().RevisionQueued();

        // Act
        queued.Block([12], Now);

        // Assert
        queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionBlocked>().IssueId.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenBlockedWithoutBlockers_Throws()
    {
        // Arrange
        RevisionQueuedIssue queued = new IssueBuilder().RevisionQueued();

        // Act
        Action act = () => queued.Block([], Now);

        // Assert
        act.ShouldThrow<InvalidOperationException>();
    }
}
