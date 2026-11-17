using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.IssueBodyHashTests;

public sealed class FromStored
{
    [Fact]
    public void WhenRestoringAStoredHash_EqualsTheHashOfTheSameBody()
    {
        // Arrange
        IssueBodyHash original = IssueBodyHash.Of("body");

        // Act
        IssueBodyHash restored = IssueBodyHash.FromStored(original.Value);

        // Assert
        restored.ShouldBe(original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("ba7816bf")]
    public void WhenTheStoredValueIsNotALowercaseHexSha256_Throws(string stored)
    {
        // Act / Assert
        Should.Throw<FormatException>(() => IssueBodyHash.FromStored(stored));
    }
}
