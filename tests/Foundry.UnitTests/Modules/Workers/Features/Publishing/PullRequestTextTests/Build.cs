using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class Build
{
    private const string IssueTitle = "Retry webhook delivery";
    private static readonly PullRequestSummary Summary = PullRequestSummary.TryCreate("add retry to webhook delivery")!;
    private static readonly PullRequestDescription Description = Descriptions.Create();

    [Theory]
    [InlineData(nameof(PullRequestKind.Feature), "feat")]
    [InlineData(nameof(PullRequestKind.Bug), "fix")]
    [InlineData(nameof(PullRequestKind.Refactor), "refactor")]
    [InlineData(nameof(PullRequestKind.Documentation), "docs")]
    [InlineData(nameof(PullRequestKind.Unclassified), "feat")]
    public void WhenTheVerdictHasASummary_TheTitleIsTheKindTypeAndTheSummary(string kindName, string type)
    {
        // Act
        PullRequestText text = PullRequestText.Build(
            Enum.Parse<PullRequestKind>(kindName), Summary, Description, IssueTitle, 42, [], [], []);

        // Assert
        text.Title.ShouldBe($"{type}: add retry to webhook delivery");
    }

    [Fact]
    public void WhenTheSummaryIsMissing_TheTitleFallsBackToTheIssueTitle()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Bug, null, Description, IssueTitle, 42, [], [], []);

        // Assert
        text.Title.ShouldBe("fix: Retry webhook delivery");
    }

    [Fact]
    public void WhenTheIssueTitleSpansLinesOrRepeatsWhitespace_TheFallbackTitleIsOneCollapsedLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(
            PullRequestKind.Feature, null, Description, "  Retry   webhook\r\n delivery\t", 42, [], [], []);

        // Assert
        text.Title.ShouldBe("feat: Retry webhook delivery");
    }

    [Fact]
    public void WhenTheIssueTitleIsLongerThanASummary_TheFallbackTitleIsTruncatedAtAWordWithAnEllipsis()
    {
        // Arrange
        string issueTitle = string.Join(' ', Enumerable.Repeat("webhook", 20));

        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, issueTitle, 42, [], [], []);

        // Assert
        string summary = text.Title["feat: ".Length..];
        summary.ShouldSatisfyAllConditions(
            () => summary.Length.ShouldBeLessThanOrEqualTo(PullRequestSummary.MaxLength),
            () => summary.ShouldEndWith("webhook…"),
            () => summary.ShouldStartWith("webhook webhook"));
    }

    [Fact]
    public void WhenTheIssueTitleIsBlank_TheFallbackTitleNamesTheIssue()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, null, "   ", 42, [], [], []);

        // Assert
        text.Title.ShouldBe("feat: resolve issue #42");
    }

    [Fact]
    public void AFreshPullRequestWithNothingToCheck_HasNoAlertAndListsChangesAndVerificationThenTheClosesLine()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(
            changes: ["Retries webhook delivery with backoff.", "Logs each attempt."],
            verification: ["dotnet test: 12 passed."]);

        // Act
        PullRequestText text = PullRequestText.Build(
            PullRequestKind.Feature, Summary, description, IssueTitle, 42, ["feat: add retry"], [], []);

        // Assert
        text.Body.ShouldBe("""
            **Changes**
            - Retries webhook delivery with backoff.
            - Logs each attempt.

            **Verified**
            - dotnet test: 12 passed.

            Closes #42
            """);
    }

    [Fact]
    public void APullRequestBody_HasNoRunKindHeading()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(cause: "The retry loop never backed off.");

        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Bug, Summary, description, IssueTitle, 42, [], [], []);

        // Assert
        text.Body.ShouldStartWith("**Cause:** The retry loop never backed off.\n\n**Changes**\n");
    }

    [Fact]
    public void WhenTheDescriptionHasNotes_TheyFollowTheVerificationCollapsed()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(notes: "## Migration\n\n- drops `pull_request_body`");

        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, Summary, description, IssueTitle, 42, [], [], []);

        // Assert
        text.Body.ShouldEndWith("""
            - dotnet test: 12 passed.

            <details><summary>Implementation notes</summary>

            ## Migration

            - drops `pull_request_body`

            </details>

            Closes #42
            """);
    }

    [Fact]
    public void WhenTheDescriptionIsMissing_TheBodyListsTheCommitSubjectsAsChangesThenTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(
            PullRequestKind.Feature, Summary, null, IssueTitle, 7, ["feat: add retry", "  ", " test: cover retry "], [], []);

        // Assert
        text.Body.ShouldBe("**Changes**\n- feat: add retry\n- test: cover retry\n\nCloses #7");
    }

    [Fact]
    public void WhenTheDescriptionAndCommitSubjectsAreMissing_TheBodyIsANeutralSentenceThenTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, null, IssueTitle, 7, [], [], []);

        // Assert
        text.Body.ShouldBe($"{PullRequestText.NoChangesListedSentence}\n\nCloses #7");
    }
}
