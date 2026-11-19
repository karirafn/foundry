using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.AddedLineRangeTests;

public sealed class Create
{
    [Fact]
    public void WhenThePathIsEmpty_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => new AddedLineRange(string.Empty, 1, 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 3)]
    [InlineData(5, 4)]
    public void WhenTheRangeIsNotAPositiveAscendingSpan_Throws(int start, int end)
    {
        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() => new AddedLineRange("src/a.cs", start, end));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    public void Contains_IsTrueOnlyForALineInsideTheRange(int line, bool expected)
    {
        // Act / Assert
        new AddedLineRange("src/a.cs", 4, 6).Contains("src/a.cs", line).ShouldBe(expected);
    }

    [Fact]
    public void Contains_IsFalseForAnotherPath()
    {
        // Act / Assert
        new AddedLineRange("src/a.cs", 4, 6).Contains("src/b.cs", 5).ShouldBeFalse();
    }
}
