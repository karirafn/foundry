using Foundry.Shared.Markdown;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.ModelMarkdownTests;

public sealed class Inline
{
    private const string Zwsp = "\u200B";

    [Theory]
    [InlineData("ask @user", "ask @" + Zwsp + "user")]
    [InlineData("ask @org/team", "ask @" + Zwsp + "org/team")]
    [InlineData("mail email@example.com", "mail email@" + Zwsp + "example.com")]
    [InlineData("@all please", "@" + Zwsp + "all please")]
    [InlineData("escaped \\@user", "escaped \\@" + Zwsp + "user")]
    [InlineData("entity &#64;user", "entity &#64;" + Zwsp + "user")]
    [InlineData("entity &#x40;user", "entity &#x40;" + Zwsp + "user")]
    [InlineData("entity &commat;user", "entity &commat;" + Zwsp + "user")]
    public void NeutralizesEveryMentionOutsideCode(string text, string expected)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("ends </details> here", "ends &lt;/details&gt; here")]
    [InlineData("<img src=x>", "&lt;img src=x&gt;")]
    [InlineData("a < b > c", "a &lt; b &gt; c")]
    [InlineData("already &lt; written", "already &lt; written")]
    public void WritesAngleBracketsAsEntitiesOutsideCode(string text, string expected)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("# heading", "\\# heading")]
    [InlineData("1. item", "1\\. item")]
    [InlineData("12) item", "12\\) item")]
    [InlineData("- item", "\\- item")]
    [InlineData("+ item", "\\+ item")]
    [InlineData("* item", "\\* item")]
    [InlineData("  - indented", "  \\- indented")]
    [InlineData("> quote", "&gt; quote")]
    [InlineData("first\n# second", "first\n\\# second")]
    [InlineData("a # b - c 1. d", "a # b - c 1. d")]
    [InlineData("2024 was a year", "2024 was a year")]
    public void EscapesALineLeadingBlockMarker(string text, string expected)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("`@user`")]
    [InlineData("see `<b>` and ``a`@b`` here")]
    [InlineData("call `List<T>`")]
    public void LeavesCodeSpansUnchanged(string text)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(text);
    }

    [Theory]
    [InlineData("an ` unclosed @user <b>", "an ` unclosed @" + Zwsp + "user &lt;b&gt;")]
    [InlineData("``mismatched` @user", "``mismatched` @" + Zwsp + "user")]
    [InlineData("\\`not code @user`", "\\`not code @" + Zwsp + "user`")]
    public void TreatsABacktickRunWithoutAnEqualClosingRunAsText(string text, string expected)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(expected);
    }

    [Fact]
    public void WhenAnEarlierLineLeavesABacktickOpen_TreatsLaterCodeSpansAsTextUntilABlankLine()
    {
        // CommonMark pairs the first line's backtick with the second line's first one, so "@user" there is outside code.
        string text = "open `here\nclose` @user `x`\n\n`@kept`";

        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe("open `here\nclose` @" + Zwsp + "user `x`\n\n`@kept`");
    }

    [Fact]
    public void WhenACodeSpanHoldsAPipe_TreatsItAsTextBecauseATableCellCanSplitIt()
    {
        // Act
        string sanitized = ModelMarkdown.Inline("| `a | b` `@user` |");

        // Assert
        sanitized.ShouldBe("| `a | b` `@" + Zwsp + "user` |");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plain text with no special characters.")]
    public void LeavesTextWithoutSpecialCharactersUnchanged(string text)
    {
        // Act
        string sanitized = ModelMarkdown.Inline(text);

        // Assert
        sanitized.ShouldBe(text);
    }
}
