using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.BlockerLinks;

public sealed class SingleLineTextTests
{
    [Fact]
    public void Cap_TrimsAndFlattensLineBreaksToSpaces()
    {
        // Arrange
        const string text = "  first\r\nsecond\nthird  ";

        // Act
        string result = SingleLineText.Cap(text, 100);

        // Assert
        result.ShouldBe("first second third");
    }

    [Fact]
    public void Cap_KeepsTextOfExactlyTheMaximumLength()
    {
        // Arrange
        string text = new('a', 500);

        // Act
        string result = SingleLineText.Cap(text, 500);

        // Assert
        result.ShouldBe(text);
    }

    [Fact]
    public void Cap_ShortensLongerTextToTheMaximumLengthEndingInAnEllipsis()
    {
        // Arrange
        string text = new('a', 600);

        // Act
        string result = SingleLineText.Cap(text, 500);

        // Assert
        result.ShouldBe($"{new string('a', 499)}…");
    }

    [Fact]
    public void Cap_WhenTheCutFallsInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange
        string emoji = char.ConvertFromUtf32(0x1F600);
        string text = new string('a', 498) + emoji + new string('b', 10);

        // Act
        string result = SingleLineText.Cap(text, 500);

        // Assert
        result.ShouldBe($"{new string('a', 498)}…");
    }
}
