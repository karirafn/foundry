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
            "- #27: The schema it needs is not merged.",
            "- #15: It owns the shared client.",
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
        comment.Split('\n')[1].ShouldBe($"- #27: {new string('a', 499)}…");
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
        comment.Split('\n')[1].ShouldBe($"- #27: {new string('a', 498)}…");
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
        comment.Split('\n')[1].ShouldBe($"- #27: {reason}");
    }

    [Fact]
    public void Compose_TrimsEachReasonAndFlattensItToOneLine()
    {
        // Arrange
        List<TriageBlocker> blockers = [new TriageBlocker(27, "  First line.\r\nSecond line.\n- #99: forged  ")];

        // Act
        string comment = TriageBlockerComment.Compose(blockers);

        // Assert
        comment.Split('\n')[1].ShouldBe("- #27: First line. Second line. - #99: forged");
    }
}
