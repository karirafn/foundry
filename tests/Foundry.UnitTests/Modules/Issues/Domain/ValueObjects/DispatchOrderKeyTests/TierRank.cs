using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class TierRank
{
    [Fact]
    public void WhenRevisionQueued_TierRankIsZero()
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();

        // Act
        int tierRank = revisionQueued.TierRank;

        // Assert
        tierRank.ShouldBe(0);
    }

    [Fact]
    public void WhenContinuationQueued_TierRankIsOne()
    {
        // Arrange
        ContinuationQueuedIssue continuationQueued = new IssueBuilder().ContinuableFailed().Retry();

        // Act
        int tierRank = continuationQueued.TierRank;

        // Assert
        tierRank.ShouldBe(1);
    }

    [Fact]
    public void WhenFreshQueued_TierRankIsTwo()
    {
        // Arrange
        FreshQueuedIssue queued = new IssueBuilder().FreshQueued();

        // Act
        int tierRank = queued.TierRank;

        // Assert
        tierRank.ShouldBe(2);
    }
}
