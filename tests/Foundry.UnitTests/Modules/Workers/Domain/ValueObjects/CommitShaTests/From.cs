using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.CommitShaTests;

public sealed class From
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void WhenAFullSha1OrSha256Name_KeepsIt(string value)
    {
        // Act
        CommitSha sha = CommitSha.From(value);

        // Assert
        sha.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    [InlineData(" 0123456789abcdef0123456789abcdef01234567")]
    public void WhenNotAFullLowercaseHexName_Throws(string value)
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => CommitSha.From(value));
        CommitSha.TryFrom(value, out _).ShouldBeFalse();
    }
}
