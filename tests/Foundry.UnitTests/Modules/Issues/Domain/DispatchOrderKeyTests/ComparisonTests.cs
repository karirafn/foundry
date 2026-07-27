using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.DispatchOrderKeyTests;

public sealed class ComparisonTests
{
    private static readonly DateTimeOffset BaseTime = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IssueId LowerId = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
    private static readonly IssueId HigherId = IssueId.From(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

    [Fact]
    public void WhenTierRankLower_SortsBefore_EvenWithWorsePositionAndDetectedAt()
    {
        // Arrange — revision (tier 0) with higher position and later time vs fresh (tier 2) with position 0
        DispatchOrderKey revision = new(TierRank: 0, Position: 999, DetectedAt: BaseTime.AddDays(100), Id: HigherId);
        DispatchOrderKey fresh = new(TierRank: 2, Position: 0, DetectedAt: BaseTime, Id: LowerId);

        // Act
        int result = revision.CompareTo(fresh);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankEqual_OrdersByPosition()
    {
        // Arrange
        DispatchOrderKey first = new(TierRank: 1, Position: 1, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey second = new(TierRank: 1, Position: 2, DetectedAt: BaseTime, Id: LowerId);

        // Act
        int result = first.CompareTo(second);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankAndPositionEqual_OrdersByDetectedAt()
    {
        // Arrange
        DispatchOrderKey earlier = new(TierRank: 1, Position: 1, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey later = new(TierRank: 1, Position: 1, DetectedAt: BaseTime.AddSeconds(1), Id: LowerId);

        // Act
        int result = earlier.CompareTo(later);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankPositionAndDetectedAtEqual_OrdersById()
    {
        // Arrange — ordinal string ordering: "00000000-..." < "ffffffff-..."
        DispatchOrderKey withLowerId = new(TierRank: 0, Position: 0, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey withHigherId = new(TierRank: 0, Position: 0, DetectedAt: BaseTime, Id: HigherId);

        // Act
        int result = withLowerId.CompareTo(withHigherId);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenAllFieldsEqual_ReturnsZero()
    {
        // Arrange
        DispatchOrderKey left = new(TierRank: 0, Position: 5, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey right = new(TierRank: 0, Position: 5, DetectedAt: BaseTime, Id: LowerId);

        // Act
        int result = left.CompareTo(right);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void WhenShuffled_SortsToExpectedDispatchOrder()
    {
        // Arrange — mixed tiers, positions, times; expected order after sort:
        // 1. revision tier=0, pos=1 (lowest tier, first position)
        // 2. continuation tier=1, pos=1 (same tier rank, earlier time)
        // 3. continuation tier=1, pos=1, later time
        // 4. fresh tier=2, pos=1 (highest tier)
        // 5. fresh tier=2, pos=2 (same tier, higher position)
        DispatchOrderKey revisionFirstRepo = new(TierRank: 0, Position: 1, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey continuationFirstRepoEarlier = new(
            TierRank: 1,
            Position: 1,
            DetectedAt: BaseTime,
            Id: LowerId);
        DispatchOrderKey continuationFirstRepoLater = new(
            TierRank: 1,
            Position: 1,
            DetectedAt: BaseTime.AddSeconds(10),
            Id: LowerId);
        DispatchOrderKey freshFirstRepo = new(TierRank: 2, Position: 1, DetectedAt: BaseTime, Id: LowerId);
        DispatchOrderKey freshSecondRepo = new(TierRank: 2, Position: 2, DetectedAt: BaseTime, Id: LowerId);

        List<DispatchOrderKey> shuffled =
        [
            freshSecondRepo,
            continuationFirstRepoLater,
            freshFirstRepo,
            revisionFirstRepo,
            continuationFirstRepoEarlier,
        ];

        // Act
        List<DispatchOrderKey> sorted = [..shuffled];
        sorted.Sort();

        // Assert
        sorted.ShouldBe(
        [
            revisionFirstRepo,
            continuationFirstRepoEarlier,
            continuationFirstRepoLater,
            freshFirstRepo,
            freshSecondRepo,
        ]);
    }
}
