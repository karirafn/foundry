using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.PullRequestTextTests;

public sealed class ConfirmedFindings
{
    private static readonly PullRequestDescription Description = PullRequestDescription.TryCreate("Adds the store.")!;

    private static readonly FalsePositiveConfirmation Confirmation = new(
        [
            new SecretFinding("src/store.ts", 3, CommitSha.From("3333333333333333333333333333333333333333"), "generic-api-key"),
            new SecretFinding("src/mix.service.spec.ts", 41, CommitSha.From("4444444444444444444444444444444444444444"), "generic-api-key"),
        ],
        new DateTimeOffset(2026, 11, 13, 16, 17, 0, TimeSpan.FromHours(1)));

    private static readonly RevertedFile[] Reverted = [new("tools/protoc.exe", RevertedFileReason.Binary, 940)];

    private const string Section = """
        ## Secret findings confirmed as false positives

        Foundry's diff gate flagged these lines as possible secrets. An operator confirmed them as false positives on 2026-11-13 15:17 UTC, so Foundry published them.

        - `src/store.ts` line 3: rule `generic-api-key`
        - `src/mix.service.spec.ts` line 41: rule `generic-api-key`
        """;

    [Fact]
    public void AConfirmedRun_ListsTheConfirmedFindingsAfterTheRevertedFilesAndBeforeTheClosesLine()
    {
        // Act
        PullRequestText text = PullRequestText.Build(
            PullRequestKind.Feature,
            null,
            Description,
            "Store",
            42,
            [],
            Reverted,
            Confirmation);

        // Assert
        text.Body.ShouldEndWith($"(940 B)\n\n{Section}\n\nCloses #42");
        text.Body.ShouldStartWith("Adds the store.\n\n## Files reverted by Foundry");
    }

    [Fact]
    public void AConfirmedRevision_ListsTheConfirmedFindingsInTheSummaryComment()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], Confirmation);

        // Assert
        comment.ShouldBe($"Adds the store.\n\n{Section}");
    }

    [Fact]
    public void ARunNeverConfirmed_HasNoConfirmedSection()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], confirmation: null);

        // Assert
        comment.ShouldBe("Adds the store.");
    }

    [Fact]
    public void TheSection_NeverCarriesTheFindingsCommit()
    {
        // Act
        string comment = PullRequestText.SummaryComment(Description, [], [], Confirmation);

        // Assert
        comment.ShouldNotContain("3333333");
    }
}
