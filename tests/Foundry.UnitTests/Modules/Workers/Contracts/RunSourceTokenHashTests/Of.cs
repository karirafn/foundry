using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.RunSourceTokenHashTests;

public sealed class Of
{
    [Fact]
    public void IsTheLowercaseHexSha256OfTheTokensUtf8Bytes()
    {
        // Act — the FIPS 180-2 test vector for "abc".
        RunSourceTokenHash hash = RunSourceTokenHash.Of("abc");

        // Assert
        hash.Value.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void RoundTripsThroughItsStoredForm()
    {
        // Arrange
        RunSourceTokenHash hash = RunSourceTokenHash.Of("token");

        // Act
        RunSourceTokenHash restored = RunSourceTokenHash.FromStored(hash.Value);

        // Assert
        restored.ShouldBe(hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("ba7816bf")]
    public void FromStoredRejectsAnythingButSixtyFourLowercaseHexCharacters(string stored)
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => RunSourceTokenHash.FromStored(stored));
    }
}
