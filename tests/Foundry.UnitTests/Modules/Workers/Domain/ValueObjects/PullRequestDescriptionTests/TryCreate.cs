using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PullRequestDescriptionTests;

public sealed class TryCreate
{
    [Theory]
    [InlineData("🤖 Generated with [Claude Code](https://claude.com/claude-code)")]
    [InlineData("Generated with [Claude Code](https://claude.ai/code)")]
    [InlineData("🤖Generated with [Claude Code](https://claude.com/claude-code)  ")]
    public void WhenTheBodyEndsWithTheClaudeCodeFooter_RemovesItAndKeepsTheRestIntact(string footer)
    {
        // Arrange
        string body = $"## Summary\n\n- Adds retry to webhook delivery.\n- Logs each attempt.\n\n{footer}\n";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value
            .ShouldBe("## Summary\n\n- Adds retry to webhook delivery.\n- Logs each attempt.");
    }

    [Fact]
    public void WhenTheBodyDescribesClaudeCliFlags_LeavesItUntouched()
    {
        // Arrange
        const string body =
            "Passes `claude -p` the `--json-schema` flag.\n\nThe worker runs `claude -p --output-format stream-json`; generated with care.";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value.ShouldBe(body);
    }

    [Fact]
    public void WhenALineMentionsClaudeCodeWithoutBeingTheFooter_KeepsIt()
    {
        // Arrange
        const string body = "Generated with [Claude Code](https://claude.com/claude-code) and reviewed by hand.";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value.ShouldBe(body);
    }

    [Fact]
    public void WhenTheBodyCarriesATrailerNamingAHuman_RemovesTheTrailer()
    {
        // Arrange
        const string body = "Fixes the retry loop.\n\nCo-Authored-By: Jane Doe <jane@example.com>";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value.ShouldBe("Fixes the retry loop.");
    }

    [Theory]
    [InlineData("co-authored-by: Claude <noreply@anthropic.com>")]
    [InlineData("CO-AUTHORED-BY: Claude <noreply@anthropic.com>")]
    [InlineData("Co-authored-by: Claude Opus <noreply@anthropic.com>")]
    public void WhenTheTrailerKeyIsInAnyCase_RemovesTheTrailer(string trailer)
    {
        // Arrange
        string body = $"Fixes the retry loop.\n\n{trailer}";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value.ShouldBe("Fixes the retry loop.");
    }

    [Fact]
    public void WhenTheBodyIsOnlyAttribution_ReturnsNull()
    {
        // Arrange
        const string body =
            "\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t\n")]
    public void WhenTheBodyIsMissingOrBlank_ReturnsNull(string? body)
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public void WhenTheBodyUsesCrLfLineEndings_NormalisesThemAndTrimsSurroundingBlankLines()
    {
        // Arrange — two trailing spaces are a Markdown hard line break, so inner lines keep them.
        const string body = "\r\n\r\nFirst line.  \r\nSecond line.  \r\n\r\n";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(body);

        // Assert
        description.ShouldNotBeNull().Value.ShouldBe("First line.  \nSecond line.");
    }

    [Fact]
    public void WhenTheBodyIsLongerThanTheMaximum_ReturnsNull()
    {
        // Act
        PullRequestDescription? description =
            PullRequestDescription.TryCreate(new string('a', PullRequestDescription.MaxLength + 1));

        // Assert
        description.ShouldBeNull();
    }
}
