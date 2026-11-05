using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DispatchOrderKeyTests;

public sealed class ComparisonTests
{
    private static readonly IssueId LowerId = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
    private static readonly IssueId HigherId = IssueId.From(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

    [Fact]
    public void WhenTierRankLower_SortsBefore_EvenWithWorsePositionAndIssueNumber()
    {
        // Arrange — revision (tier 0) with higher position and higher issue number vs fresh (tier 2) with position 0
        DispatchOrderKey revision = new(TierRank: 0, Position: 999, IssueNumber: 999, Id: HigherId);
        DispatchOrderKey fresh = new(TierRank: 2, Position: 0, IssueNumber: 1, Id: LowerId);

        // Act
        int result = revision.CompareTo(fresh);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankEqual_OrdersByPosition()
    {
        // Arrange
        DispatchOrderKey first = new(TierRank: 1, Position: 1, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey second = new(TierRank: 1, Position: 2, IssueNumber: 1, Id: LowerId);

        // Act
        int result = first.CompareTo(second);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankAndPositionEqual_OrdersByIssueNumber()
    {
        // Arrange
        DispatchOrderKey lowerNumber = new(TierRank: 1, Position: 1, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey higherNumber = new(TierRank: 1, Position: 1, IssueNumber: 2, Id: LowerId);

        // Act
        int result = lowerNumber.CompareTo(higherNumber);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenTierRankPositionAndIssueNumberEqual_OrdersById()
    {
        // Arrange — ordinal string ordering: "00000000-..." < "ffffffff-..."
        DispatchOrderKey withLowerId = new(TierRank: 0, Position: 0, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey withHigherId = new(TierRank: 0, Position: 0, IssueNumber: 1, Id: HigherId);

        // Act
        int result = withLowerId.CompareTo(withHigherId);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenAllFieldsEqual_ReturnsZero()
    {
        // Arrange
        DispatchOrderKey left = new(TierRank: 0, Position: 5, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey right = new(TierRank: 0, Position: 5, IssueNumber: 1, Id: LowerId);

        // Act
        int result = left.CompareTo(right);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void WhenShuffled_SortsToExpectedDispatchOrder()
    {
        // Arrange — mixed tiers, positions, issue numbers; expected order after sort:
        // 1. revision tier=0, pos=1 (lowest tier, first position)
        // 2. continuation tier=1, pos=1 (same tier rank, lower issue number)
        // 3. continuation tier=1, pos=1, higher issue number
        // 4. fresh tier=2, pos=1 (highest tier)
        // 5. fresh tier=2, pos=2 (same tier, higher position)
        DispatchOrderKey revisionFirstRepo = new(TierRank: 0, Position: 1, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey continuationFirstRepoLowerNumber = new(
            TierRank: 1,
            Position: 1,
            IssueNumber: 1,
            Id: LowerId);
        DispatchOrderKey continuationFirstRepoHigherNumber = new(
            TierRank: 1,
            Position: 1,
            IssueNumber: 2,
            Id: LowerId);
        DispatchOrderKey freshFirstRepo = new(TierRank: 2, Position: 1, IssueNumber: 1, Id: LowerId);
        DispatchOrderKey freshSecondRepo = new(TierRank: 2, Position: 2, IssueNumber: 1, Id: LowerId);

        List<DispatchOrderKey> shuffled =
        [
            freshSecondRepo,
            continuationFirstRepoHigherNumber,
            freshFirstRepo,
            revisionFirstRepo,
            continuationFirstRepoLowerNumber,
        ];

        // Act
        List<DispatchOrderKey> sorted = [..shuffled];
        sorted.Sort();

        // Assert
        sorted.ShouldBe(
        [
            revisionFirstRepo,
            continuationFirstRepoLowerNumber,
            continuationFirstRepoHigherNumber,
            freshFirstRepo,
            freshSecondRepo,
        ]);
    }
}
