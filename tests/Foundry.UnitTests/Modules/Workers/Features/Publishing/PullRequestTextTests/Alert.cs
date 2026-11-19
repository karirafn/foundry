using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class Alert
{
    private const string Zwsp = "\u200B";

    private static readonly RevertedFile[] TwoReverted =
    [
        new("assets/demo.mp4", RevertedFileReason.Oversized, 50_540_134),
        new("tools/protoc.exe", RevertedFileReason.Binary, 940),
    ];

    private static readonly SecretFinding[] ThreeFindings =
    [
        new("src/a.ts", 1, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
        new("src/b.ts", 2, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
        new("src/c.ts", 3, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
    ];

    [Fact]
    public void WhenTheRunNamesWhatAReviewerMustCheck_TheBodyLeadsWithAWarningListingIt()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(
            attention: ["The CI log could not be read.", "Production has the same race."]);

        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Bug, null, description, "Race", 42, [], [], []);

        // Assert
        text.Body.ShouldStartWith("""
            > [!WARNING]
            > - The CI log could not be read.
            > - Production has the same race.

            **Changes**
            """);
    }

    [Fact]
    public void WhenTheDiffGateRevertedFilesAndMatchedGenericSecrets_TheWarningCountsThem()
    {
        // Act
        PullRequestText text = PullRequestText.Build(
            PullRequestKind.Feature, null, Descriptions.Create(), "Demo", 42, [], TwoReverted, ThreeFindings);

        // Assert
        text.Body.ShouldStartWith("""
            > [!WARNING]
            > - 2 files were reverted by Foundry's diff gate and are missing from this change.
            > - 3 lines matched a generic secret pattern; check none is a real secret.

            **Changes**
            """);
    }

    [Fact]
    public void WhenTheDiffGateFoundOneOfEach_TheWarningUsesTheSingular()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Descriptions.Create(), [], [TwoReverted[0]], [ThreeFindings[0]]);

        // Assert
        comment.ShouldStartWith("""
            > [!WARNING]
            > - 1 file was reverted by Foundry's diff gate and is missing from this change.
            > - 1 line matched a generic secret pattern; check it is not a real secret.

            """);
    }

    [Fact]
    public void WhenBothTheRunAndTheDiffGateRaiseSomething_OneWarningListsTheDiffGateFactsFirst()
    {
        // Arrange
        PullRequestDescription description = Descriptions.Create(attention: ["Not run against @team's <staging>."]);

        // Act
        string comment = PullRequestText.SummaryComment(null, description, [], TwoReverted, ThreeFindings);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldStartWith($"""
                > [!WARNING]
                > - 2 files were reverted by Foundry's diff gate and are missing from this change.
                > - 3 lines matched a generic secret pattern; check none is a real secret.
                > - Not run against @{Zwsp}team's &lt;staging&gt;.

                **Changes**
                """),
            () => comment.Split("[!").Length.ShouldBe(2));
    }

    [Fact]
    public void WhenNothingNeedsChecking_ThereIsNoAlert()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Descriptions.Create(), [], [], []);

        // Assert
        comment.ShouldNotContain("[!WARNING]");
    }

    [Fact]
    public void WithoutADescription_TheDiffGateFactsStillLeadAsAWarning()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, null, ["feat: add demo"], TwoReverted, []);

        // Assert
        comment.ShouldStartWith("""
            > [!WARNING]
            > - 2 files were reverted by Foundry's diff gate and are missing from this change.

            **Changes**
            - feat: add demo
            """);
    }
}
