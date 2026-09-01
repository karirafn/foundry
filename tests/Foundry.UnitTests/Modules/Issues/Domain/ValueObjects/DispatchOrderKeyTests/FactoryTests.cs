using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class FactoryTests
{
    [Fact]
    public void WhenFreshQueued_BuildsKeyWithTierRankTwo()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        FreshQueuedIssue freshQueued = new IssueBuilder().WithDetectedAt(detectedAt).FreshQueued();
        int position = 3;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(freshQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(2),
            () => key.Position.ShouldBe(3),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(freshQueued.Id));
    }

    [Fact]
    public void WhenRevisionQueued_BuildsKeyWithTierRankZero()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        RevisionQueuedIssue revisionQueued = new IssueBuilder()
            .WithDetectedAt(detectedAt)
            .WithReviewComments([new ReviewComment("Please fix the formatting.")])
            .RevisionQueued();
        int position = 1;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(revisionQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(0),
            () => key.Position.ShouldBe(1),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(revisionQueued.Id));
    }

    [Fact]
    public void WhenContinuationQueued_BuildsKeyWithTierRankOne()
    {
        // Arrange
        DateTimeOffset detectedAt = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        ContinuationQueuedIssue continuationQueued = new IssueBuilder()
            .WithDetectedAt(detectedAt)
            .ContinuableFailed()
            .Retry();
        int position = 2;

        // Act
        DispatchOrderKey key = DispatchOrderKey.For(continuationQueued, position);

        // Assert
        key.ShouldSatisfyAllConditions(
            () => key.TierRank.ShouldBe(1),
            () => key.Position.ShouldBe(2),
            () => key.DetectedAt.ShouldBe(detectedAt),
            () => key.Id.ShouldBe(continuationQueued.Id));
    }
}
