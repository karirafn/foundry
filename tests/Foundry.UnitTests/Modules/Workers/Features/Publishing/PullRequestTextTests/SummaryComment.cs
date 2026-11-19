using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class SummaryComment
{
    private const string Zwsp = "\u200B";

    [Fact]
    public void ARevisionWithACause_LeadsWithTheRunKindHeadingAndTheCauseAndHasNoClosesLine()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(
            cause: "The build failed because a test asserted the old option name.",
            changes: ["Renames the option in the test."],
            verification: ["dotnet test: 40 passed."]);

        // Act
        string comment = PullRequestText.SummaryComment(IssueSummary.ChecksFixRunKind, description, [], [], []);

        // Assert
        comment.ShouldBe("""
            ### Checks fix
            **Cause:** The build failed because a test asserted the old option name.

            **Changes**
            - Renames the option in the test.

            **Verified**
            - dotnet test: 40 passed.
            """);
    }

    [Theory]
    [InlineData(IssueSummary.ChecksFixRunKind, "Checks fix")]
    [InlineData(IssueSummary.ReviewFixRunKind, "Review fix")]
    [InlineData(IssueSummary.FindingsFixRunKind, "Review findings fix")]
    [InlineData(IssueSummary.ConflictFixRunKind, "Conflict fix")]
    [InlineData(IssueSummary.InputResumeRunKind, "Resumed with input")]
    [InlineData(IssueSummary.ContinuationRunKind, "Continuation")]
    [InlineData(IssueSummary.FreshRunKind, "Update")]
    public void TheHeading_NamesTheRunKind(string runKind, string heading)
    {
        // Act
        string comment = PullRequestText.SummaryComment(runKind, Descriptions.Create(), [], [], []);

        // Assert
        comment.ShouldStartWith($"### {heading}\n\n**Changes**\n");
    }

    [Fact]
    public void WhenTheRunKindIsUnknown_TheCommentHasNoHeading()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Descriptions.Create(), [], [], []);

        // Assert
        comment.ShouldStartWith("**Changes**\n");
    }

    [Fact]
    public void WhenTheDescriptionIsMissing_TheCommentListsTheCommitSubjectsAsChanges()
    {
        // Act
        string comment = PullRequestText.SummaryComment(
            IssueSummary.ReviewFixRunKind, null, ["fix: rename the option", "test: cover the rename"], [], []);

        // Assert
        comment.ShouldBe("### Review fix\n\n**Changes**\n- fix: rename the option\n- test: cover the rename");
    }

    [Fact]
    public void WhenTheDescriptionAndCommitSubjectsAreMissing_TheCommentIsTheNeutralSentence()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, null, [], [], []);

        // Assert
        comment.ShouldBe(PullRequestText.NoChangesListedSentence);
    }

    [Fact]
    public void WhenTheNotesCloseTheDetailsOrMentionSomeone_BothAreNeutralizedAndTheSectionStaysWhole()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(notes: "Asks @alice.\n\n</details>\n\n- still a list");

        // Act
        string comment = PullRequestText.SummaryComment(null, description, [], [], []);

        // Assert
        comment.ShouldEndWith($"""
            <details><summary>Implementation notes</summary>

            Asks @{Zwsp}alice.

            &lt;/details&gt;

            - still a list

            </details>
            """);
    }

    [Fact]
    public void WhenAChangeOrTheCauseMentionsSomeoneOrHoldsHtml_BothAreNeutralized()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(
            cause: "@bob saw <b>it</b>",
            changes: ["# thanks @alice for <img src=x>"]);

        // Act
        string comment = PullRequestText.SummaryComment(null, description, [], [], []);

        // Assert
        comment.ShouldStartWith(
            "**Cause:** @\u200Bbob saw &lt;b&gt;it&lt;/b&gt;\n\n**Changes**\n- \\# thanks @\u200Balice for &lt;img src=x&gt;\n");
    }

    [Fact]
    public void WhenACommitSubjectMentionsSomeoneOrHoldsHtml_BothAreNeutralized()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, null, ["fix: thank @alice", "# <img src=x>"], [], []);

        // Assert
        comment.ShouldBe("**Changes**\n- fix: thank @\u200Balice\n- \\# &lt;img src=x&gt;");
    }
}
