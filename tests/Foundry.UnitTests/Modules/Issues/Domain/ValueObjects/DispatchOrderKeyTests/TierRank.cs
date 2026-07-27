using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class TierRank
{
    [Fact]
    public void WhenRevisionQueued_TierRankIsZero()
    {
        // Arrange (TierRank is type-level — no instance needed)

        // Act
        int tierRank = RevisionQueuedIssue.TierRank;

        // Assert
        tierRank.ShouldBe(0);
    }

    [Fact]
    public void WhenContinuationQueued_TierRankIsOne()
    {
        // Arrange (TierRank is type-level — no instance needed)

        // Act
        int tierRank = ContinuationQueuedIssue.TierRank;

        // Assert
        tierRank.ShouldBe(1);
    }

    [Fact]
    public void WhenFreshQueued_TierRankIsTwo()
    {
        // Arrange (TierRank is type-level — no instance needed)

        // Act
        int tierRank = QueuedIssue.TierRank;

        // Assert
        tierRank.ShouldBe(2);
    }
}
