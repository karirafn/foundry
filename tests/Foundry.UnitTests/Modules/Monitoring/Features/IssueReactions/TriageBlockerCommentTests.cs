using Foundry.Modules.Monitoring.Features.IssueReactions;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.IssueReactions;

public sealed class TriageBlockerCommentTests
{
    [Fact]
    public void Compose_ListsEveryBlockerInOrderBetweenTheOpeningAndClosingLines()
    {
        // Arrange
        List<TriageBlocker> blockers =
        [
            new TriageBlocker(27, "The schema it needs is not merged."),
            new TriageBlocker(15, "It owns the shared client."),
        ];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.ShouldBe(string.Join(
            "\n",
            "Triage found this issue needs these open issues first, and linked them as blockers:",
            "- #27: `The schema it needs is not merged.`",
            "- #15: `It owns the shared client.`",
            "Triage runs again once they close. Remove a link to stop waiting on that issue."));
    }

    [Fact]
    public void Compose_CapsALongReasonAtFiveHundredCharactersEndingInAnEllipsis()
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, new string('a', 600))];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe($"- #27: `{new string('a', 499)}…`");
    }

    [Fact]
    public void Compose_WhenTheCapFallsInsideASurrogatePair_DropsTheWholePair()
    {
        // Arrange
        string emoji = char.ConvertFromUtf32(0x1F600);
        List<TriageBlocker> blockers = [new TriageBlocker(27, new string('a', 498) + emoji + new string('b', 10))];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe($"- #27: `{new string('a', 498)}…`");
    }

    [Fact]
    public void Compose_KeepsAReasonOfExactlyFiveHundredCharacters()
    {
        // Arrange
        string reason = new('a', 500);
        List<TriageBlocker> blockers = [new TriageBlocker(27, reason)];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe($"- #27: `{reason}`");
    }

    [Fact]
    public void Compose_TrimsEachReasonAndFlattensItToOneLine()
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, "  First line.\r\nSecond line.\n- #99: forged  ")];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe("- #27: `First line. Second line. - #99: forged`");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Compose_WhenTheReasonIsEmptyAfterTrimming_ListsTheBlockerWithoutAColonOrCodeSpan(string reason)
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(15, reason)];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe("- #15");
    }

    [Theory]
    [InlineData("ping @user and @org/team")]
    [InlineData("see [x](https://e.example) and ![](https://e.example/i.png)")]
    [InlineData("a <b>bold</b> <!-- hidden")]
    public void Compose_RendersTheReasonInsideACodeSpanSoMentionsLinksAndHtmlAreInert(string reason)
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, reason)];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe($"- #27: `{reason}`");
    }

    [Theory]
    [InlineData("run `make` first", "- #27: ``run `make` first``")]
    [InlineData("a ``b`` c", "- #27: ```a ``b`` c```")]
    public void Compose_WhenTheReasonContainsBackticks_FencesItWithALongerRun(string reason, string expectedLine)
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, reason)];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe(expectedLine);
    }

    [Theory]
    [InlineData("`make` first", "- #27: `` `make` first ``")]
    [InlineData("run `make`", "- #27: `` run `make` ``")]
    public void Compose_WhenTheReasonStartsOrEndsWithABacktick_PadsTheCodeSpanWithASpace(
        string reason,
        string expectedLine)
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, reason)];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe(expectedLine);
    }
}
