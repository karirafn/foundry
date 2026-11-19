using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class GenericFindings
{
    private static readonly PullRequestDescription Description = Descriptions.Create();

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
        <details><summary>Possible secrets (2)</summary>

        - `src/store.ts` line 3: rule `generic-api-key`
        - `docs/README.md` line 41: rule `generic-api-key`

        </details>
        """;

    [Fact]
    public void ARunWithGenericFindings_ListsThemCollapsedInThePullRequestBodyBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, "Store", 42, [], [], Findings);

        // Assert
        text.Body.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}\n\nCloses #42");
    }

    [Fact]
    public void ARevisionWithGenericFindings_ListsThemCollapsedInTheSummaryComment()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], Findings);

        // Assert
        comment.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}");
    }

    [Fact]
    public void ARunWithoutGenericFindings_HasNoGenericSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], []);

        // Assert
        comment.ShouldNotContain("Possible secrets");
    }

    [Fact]
    public void TheSection_NeverCarriesTheFindingsCommit()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], Findings);

        // Assert
        comment.ShouldSatisfyAllConditions(
            () => comment.ShouldNotContain("3333333"),
            () => comment.ShouldNotContain("4444444"));
    }

    [Fact]
    public void TheSections_ListRevertedFilesThenPossibleSecretsThenConfirmedFalsePositives()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], Reverted, Findings, Confirmation);

        // Assert
        int reverted = comment.IndexOf("<summary>Reverted files (1)</summary>", StringComparison.Ordinal);
        int generic = comment.IndexOf("<summary>Possible secrets (2)</summary>", StringComparison.Ordinal);
        int confirmed = comment.IndexOf("<summary>Confirmed false positives (1)</summary>", StringComparison.Ordinal);
        comment.ShouldSatisfyAllConditions(
            () => reverted.ShouldBeGreaterThan(0),
            () => generic.ShouldBeGreaterThan(reverted),
            () => confirmed.ShouldBeGreaterThan(generic));
    }
}
