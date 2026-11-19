using Foundry.Shared.Markdown;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.ModelMarkdownTests;

public sealed class Block
{
    private const string Zwsp = "\u200B";

    [Theory]
    [InlineData("ask @user", "ask @" + Zwsp + "user")]
    [InlineData("ask @org/team", "ask @" + Zwsp + "org/team")]
    [InlineData("mail email@example.com", "mail email@" + Zwsp + "example.com")]
    [InlineData("ends </details> here", "ends &lt;/details&gt; here")]
    [InlineData("<img src=x>", "&lt;img src=x&gt;")]
    [InlineData("- item @user", "- item @" + Zwsp + "user")]
    public void NeutralizesMentionsAndHtmlOutsideCode(string markdown, string expected)
    {
        // Act
        string sanitized = ModelMarkdown.Block(markdown);

        // Assert
        sanitized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("# heading\n\n1. item\n2. item\n\n- a\n+ b\n* c")]
    [InlineData("> quoted\n> > nested")]
    [InlineData("> [!WARNING]\n> check this")]
    [InlineData("line one  \nline two\r\nline three")]
    public void LeavesBlockMarkersAndLineBreaksAsWritten(string markdown)
    {
        // Act
        string sanitized = ModelMarkdown.Block(markdown);

        // Assert
        sanitized.ShouldBe(markdown);
    }

    [Theory]
    [InlineData("before\n\n```\n@user <b>\n```\n\nafter")]
    [InlineData("~~~csharp\nList<T> items; // @user\n~~~")]
    [InlineData("````\n```\n@user\n```\n````")]
    [InlineData("```\n@user <b> never closed")]
    [InlineData("see `@user` and `<b>`")]
    public void LeavesCodeUnchanged(string markdown)
    {
        // Act
        string sanitized = ModelMarkdown.Block(markdown);

        // Assert
        sanitized.ShouldBe(markdown);
    }

    [Fact]
    public void ResumesEscapingAfterAFencedBlockCloses()
    {
        // Act
        string sanitized = ModelMarkdown.Block("```\n@kept\n```\n@user");

        // Assert
        sanitized.ShouldBe("```\n@kept\n```\n@" + Zwsp + "user");
    }

    [Fact]
    public void WhenAFenceIsIndented_TreatsItsLinesAsTextBecauseAContainerCanEndIt()
    {
        // An unindented line closes the list item, and with it the fence, so "@user" renders as a paragraph.
        string markdown = "- item\n\n  ```\n@user\n  ```";

        // Act
        string sanitized = ModelMarkdown.Block(markdown);

        // Assert
        sanitized.ShouldBe("- item\n\n  ```\n@" + Zwsp + "user\n  ```");
    }

    [Fact]
    public void WhenABacktickIsUnclosed_EscapesTheTextAfterIt()
    {
        // Act
        string sanitized = ModelMarkdown.Block("an ` unclosed @user <b>");

        // Assert
        sanitized.ShouldBe("an ` unclosed @" + Zwsp + "user &lt;b&gt;");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plain text with no special characters.")]
    public void LeavesTextWithoutSpecialCharactersUnchanged(string markdown)
    {
        // Act
        string sanitized = ModelMarkdown.Block(markdown);

        // Assert
        sanitized.ShouldBe(markdown);
    }
}
