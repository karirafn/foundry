using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.IssueBodyHashTests;

public sealed class Of
{
    [Fact]
    public void WhenHashingABody_ReturnsTheLowercaseHexSha256OfItsUtf8Bytes()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.Of("abc");

        // Assert: the FIPS 180-2 SHA-256 test vector for "abc".
        hash.Value.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void WhenTheBodyIsEmpty_ReturnsTheHashOfNoBytes()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.Of(string.Empty);

        // Assert
        hash.Value.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Theory]
    [InlineData("line one\r\nline two")]
    [InlineData("line one\rline two")]
    [InlineData("line one\nline two  \n\n")]
    [InlineData("line one\r\nline two\t\r\n")]
    public void WhenBodiesDifferOnlyInLineEndingsOrTrailingWhitespace_TheyHashEqual(string variant)
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.Of(variant);

        // Assert
        hash.ShouldBe(IssueBodyHash.Of("line one\nline two"));
    }

    [Fact]
    public void WhenBodiesDifferInText_TheyHashDifferently()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.Of("- [x] done");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.Of("- [ ] done"));
    }

    [Fact]
    public void WhenBodiesDifferInLeadingWhitespace_TheyHashDifferently()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.Of("  indented");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.Of("indented"));
    }

    [Fact]
    public void WhenTheBodyIsNull_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentNullException>(() => IssueBodyHash.Of(null!));
    }
}
