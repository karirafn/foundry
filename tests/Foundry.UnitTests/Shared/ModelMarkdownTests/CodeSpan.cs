using Foundry.Shared.Markdown;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.ModelMarkdownTests;

public sealed class CodeSpan
{
    [Theory]
    [InlineData("plain text", "`plain text`")]
    [InlineData("a`b", "``a`b``")]
    [InlineData("a```b", "````a```b````")]
    [InlineData("`start", "`` `start ``")]
    [InlineData("end`", "`` end` ``")]
    [InlineData("", "``")]
    public void WrapsTextInAFenceLongerThanItsLongestBacktickRun(string text, string expected)
    {
        // Act
        string span = ModelMarkdown.CodeSpan(text);

        // Assert
        span.ShouldBe(expected);
    }
}
