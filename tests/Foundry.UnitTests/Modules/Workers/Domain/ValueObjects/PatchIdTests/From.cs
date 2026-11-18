using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PatchIdTests;

public sealed class From
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void WhenAFullSha1OrSha256Id_KeepsIt(string value)
    {
        // Act
        PatchId patchId = PatchId.From(value);

        // Assert
        patchId.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 0000000000000000000000000000000000000000")]
    public void WhenNotAFullLowercaseHexId_Throws(string value)
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => PatchId.From(value));
        PatchId.TryFrom(value, out _).ShouldBeFalse();
    }
}
