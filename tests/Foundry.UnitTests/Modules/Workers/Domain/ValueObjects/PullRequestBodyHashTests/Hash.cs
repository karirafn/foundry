using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PullRequestBodyHashTests;

public sealed class Hash
{
    [Fact]
    public void IsTheLowercaseHexSha256OfTheBodysUtf8Bytes()
    {
        // Act — the FIPS 180-2 test vector for "abc".
        PullRequestBodyHash hash = PullRequestBodyHash.Hash("abc");

        // Assert
        hash.Value.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void ABodyReadBackWithCrLfLineEndings_HashesLikeTheBodySentWithLf()
    {
        // Arrange
        PullRequestBodyHash sent = PullRequestBodyHash.Hash("## Summary\n\n- Adds retry.\n\nCloses #42");

        // Act
        PullRequestBodyHash readBack = PullRequestBodyHash.Hash("## Summary\r\n\r\n- Adds retry.\r\n\r\nCloses #42");

        // Assert
        readBack.ShouldBe(sent);
    }

    [Fact]
    public void TrailingWhitespace_IsIgnored()
    {
        // Arrange
        PullRequestBodyHash sent = PullRequestBodyHash.Hash("Closes #42");

        // Act
        PullRequestBodyHash readBack = PullRequestBodyHash.Hash("Closes #42\r\n \t\n");

        // Assert
        readBack.ShouldBe(sent);
    }

    [Theory]
    [InlineData("Closes  #42")]
    [InlineData(" Closes #42")]
    [InlineData("Closes #43")]
    public void AnyEditInsideTheText_ChangesTheHash(string edited)
    {
        // Act
        PullRequestBodyHash hash = PullRequestBodyHash.Hash(edited);

        // Assert
        hash.ShouldNotBe(PullRequestBodyHash.Hash("Closes #42"));
    }

    [Fact]
    public void RoundTripsThroughItsStoredForm()
    {
        // Arrange
        PullRequestBodyHash hash = PullRequestBodyHash.Hash("Closes #42");

        // Act
        PullRequestBodyHash restored = PullRequestBodyHash.FromStored(hash.Value);

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
        Should.Throw<ArgumentException>(() => PullRequestBodyHash.FromStored(stored));
    }
}
