using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class ConfirmedFindings
{
    private static readonly PullRequestDescription Description = Descriptions.Create();

    private static readonly FalsePositiveConfirmation Confirmation = new(
        [
            new SecretFinding("src/store.ts", 3, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
            new SecretFinding("src/mix.service.spec.ts", 41, CommitSha.From("4444444444444444444444444444444444444444"), "generic-api-key"),
        ],
        new DateTimeOffset(2026, 11, 13, 16, 17, 0, TimeSpan.FromHours(1)));

    private const string Section = """
        <details><summary>Confirmed false positives (2)</summary>

        An operator confirmed these possible secrets as false positives on 2026-11-13 15:17 UTC, so Foundry published them.

        - `src/store.ts` line 3: rule `generic-api-key`
        - `src/mix.service.spec.ts` line 41: rule `generic-api-key`

        </details>
        """;

    [Fact]
    public void AConfirmedRun_ListsTheConfirmedFindingsCollapsedBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(PullRequestKind.Feature, null, Description, "Store", 42, [], [], [], Confirmation);

        // Assert
        text.Body.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}\n\nCloses #42");
    }

    [Fact]
    public void AConfirmedRevision_ListsTheConfirmedFindingsInTheSummaryComment()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], [], Confirmation);

        // Assert
        comment.ShouldEndWith($"- dotnet test: 12 passed.\n\n{Section}");
    }

    [Fact]
    public void ConfirmedFindings_RaiseNoAlert()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], [], Confirmation);

        // Assert
        comment.ShouldNotContain("[!WARNING]");
    }

    [Fact]
    public void ARunNeverConfirmed_HasNoConfirmedSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], [], confirmation: null);

        // Assert
        comment.ShouldNotContain("Confirmed false positives");
    }

    [Fact]
    public void TheSection_NeverCarriesTheFindingsCommit()
    {
        // Act
        string comment = PullRequestText.SummaryComment(null, Description, [], [], [], Confirmation);

        // Assert
        comment.ShouldNotContain("3333333");
    }
}
