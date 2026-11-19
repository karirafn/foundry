using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class GenericFindings
{
    private static readonly PullRequestDescription Description = PullRequestDescription.TryCreate("Adds the store.")!;

    private static readonly SecretFinding[] Findings =
    [
        new("src/store.ts", 3, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
        new("docs/README.md", 41, CommitSha.From("4444444444444444444444444444444444444444"), "generic-api-key"),
    ];

    private static readonly RevertedFile[] Reverted = [new("tools/protoc.exe", RevertedFileReason.Binary, 940)];

    private static readonly FalsePositiveConfirmation Confirmation = new(
        [new SecretFinding("a.env", 1, CommitSha.From("5555555555555555555555555555555555555555"), "github-pat")],
        new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero));

    private const string Section = """
        ## Possible generic secrets for review

        Foundry's diff gate matched these lines with a generic secret pattern and published them; check that none of them is a real secret.

        - `src/store.ts` line 3: rule `generic-api-key`
        - `docs/README.md` line 41: rule `generic-api-key`
        """;

    [Fact]
    public void ARunWithGenericFindings_ListsThemInThePullRequestBodyBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, "Store", 42, [], [], Findings);

        // Assert
        text.Body.ShouldBe($"Adds the store.\n\n{Section}\n\nCloses #42");
    }

    [Fact]
    public void ARevisionWithGenericFindings_ListsThemInTheSummaryComment()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], Findings);

        // Assert
        comment.ShouldBe($"Adds the store.\n\n{Section}");
    }

    [Fact]
    public void ARunWithoutGenericFindings_HasNoGenericSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], []);

        // Assert
        comment.ShouldBe("Adds the store.");
    }

    [Fact]
    public void TheSection_NeverCarriesTheFindingsCommit()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], Findings);

        // Assert
        comment.ShouldNotContain("3333333");
        comment.ShouldNotContain("4444444");
    }

    [Fact]
    public void TheSection_FollowsTheRevertedFilesAndConfirmedFindingsSections()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], Reverted, Findings, Confirmation);

        // Assert
        int reverted = comment.IndexOf("## Files reverted by Foundry", StringComparison.Ordinal);
        int confirmed = comment.IndexOf("## Secret findings confirmed as false positives", StringComparison.Ordinal);
        int generic = comment.IndexOf("## Possible generic secrets for review", StringComparison.Ordinal);
        reverted.ShouldBeGreaterThan(0);
        confirmed.ShouldBeGreaterThan(reverted);
        generic.ShouldBeGreaterThan(confirmed);
        comment.ShouldEndWith(Section);
    }
}
