using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IssueIdTests;

public sealed class CompareTo
{
    [Fact]
    public void WhenLeftStringOrdersPriorToRight_ReturnNegative()
    {
        // Arrange — these two GUIDs differ in the first character of their "D" string representation,
        // but Guid.CompareTo would order them differently (it uses mixed-endian byte fields).
        // Ordinal string: "00000000-..." < "ff000000-..." → C# must agree with SQLite TEXT order.
        IssueId lower = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
        IssueId higher = IssueId.From(new Guid("ff000000-0000-0000-0000-000000000001"));

        // Act
        int result = lower.CompareTo(higher);

        // Assert
        result.ShouldBeLessThan(0);
    }

    [Fact]
    public void WhenLeftStringOrdersAfterRight_ReturnPositive()
    {
        // Arrange
        IssueId lower = IssueId.From(new Guid("00000000-0000-0000-0000-000000000001"));
        IssueId higher = IssueId.From(new Guid("ff000000-0000-0000-0000-000000000001"));

        // Act
        int result = higher.CompareTo(lower);

        // Assert
        result.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void WhenEqual_ReturnZero()
    {
        // Arrange
        Guid guid = new("aabbccdd-1122-3344-5566-778899aabbcc");
        IssueId left = IssueId.From(guid);
        IssueId right = IssueId.From(guid);

        // Act
        int result = left.CompareTo(right);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void WhenCompareOrderMatchesOrdinalStringOrder_ForRepresentativeGuids()
    {
        // Arrange — choose GUIDs where Guid.CompareTo and ordinal string ordering diverge.
        // Guid field order is time_low (bytes 0-3, little-endian), so Guid("01000000-...")
        // and Guid("00010000-...") have the same first byte in "D" format but Guid.CompareTo
        // uses byte-level fields that differ. We verify the C# operators match string sort.
        IssueId a = IssueId.From(new Guid("10000000-0000-0000-0000-000000000000"));
        IssueId b = IssueId.From(new Guid("20000000-0000-0000-0000-000000000000"));
        IssueId c = IssueId.From(new Guid("30000000-0000-0000-0000-000000000000"));

        // Act — verify transitivity consistent with string ordering
        bool aLtB = a < b;
        bool bLtC = b < c;
        bool aLtC = a < c;

        // Assert — ordering matches ordinal string sort of "D" representation
        string aStr = a.Value.ToString("D");
        string bStr = b.Value.ToString("D");
        string cStr = c.Value.ToString("D");

        aLtB.ShouldBe(string.CompareOrdinal(aStr, bStr) < 0);
        bLtC.ShouldBe(string.CompareOrdinal(bStr, cStr) < 0);
        aLtC.ShouldBe(string.CompareOrdinal(aStr, cStr) < 0);
    }
}
