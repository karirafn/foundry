using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class ReviewFactsRendering
{
    private const string Template = "You are reviewing issue #{issueNumber}.\n\n{issueContent}\n\nJudge it.";

    private static readonly PullRequestUrl Url = PullRequestUrl.From("https://github.com/acme/app/pull/7");
    private static readonly CommitSha Commit = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

    private static SuppliedReviewFacts Facts(IReadOnlyList<ChangedPath> paths, string? issueBody) =>
        SuppliedReviewFacts.Compute(Url, Commit, new ReviewPacketFacts(paths, patchId: null), issueBody);

    private static string BuildPrompt(SuppliedReviewFacts? facts) =>
        SystemPromptBuilder.BuildRestricted(
                "Principles.",
                42,
                Template,
                new IssueContent("Add retry", "Retry webhook delivery.", []),
                Toolchain.Unrecorded,
                reviewFacts: facts)
            .ShouldBeOfType<Result<string>.Success>().Value;

    [Fact]
    public void WhenReviewFactsAreSupplied_RendersEachCriterionByIdTheFlagAndTheClassifiedPaths()
    {
        // Arrange
        SuppliedReviewFacts facts = Facts(
            [new ChangedPath(ChangeStatus.Modified, "src/a.cs"), new ChangedPath(ChangeStatus.Deleted, "tests/a_test.go")],
            "## Acceptance Criteria\n\n- [ ] Retries <three> times.\n- [x] Logs each retry.\n");

        // Act
        string prompt = BuildPrompt(facts);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("## Review facts"),
            () => prompt.ShouldContain("Reviewed commit: " + Commit.Value),
            () => prompt.ShouldContain("AC1: Retries &lt;three&gt; times.\nAC2: Logs each retry.\n"),
            () => prompt.ShouldContain("Production changed, no test changed: yes."),
            () => prompt.ShouldContain("| M | production | src/a.cs |\n| D | test | tests/a_test.go |"),
            () => prompt.IndexOf("## Review facts", StringComparison.Ordinal)
                .ShouldBeGreaterThan(prompt.IndexOf("Judge it.", StringComparison.Ordinal)));
    }

    [Fact]
    public void WhenNoCriteriaWereFound_SaysSoAndTheFlagIsOff()
    {
        // Act
        string prompt = BuildPrompt(Facts([new ChangedPath(ChangeStatus.Modified, "README.md")], issueBody: "No heading."));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.NoAcceptanceCriteriaText),
            () => prompt.ShouldNotContain("<acceptance-criteria>"),
            () => prompt.ShouldContain("Production changed, no test changed: no."));
    }

    [Fact]
    public void WhenMoreThanTheCapOfPathsChanged_ListsTheCapAndNotesTheRestWhileTheFlagCountsEveryPath()
    {
        // Arrange
        List<ChangedPath> paths =
        [
            .. Enumerable.Range(0, SystemPromptBuilder.MaxReviewPathRows).Select(i => new ChangedPath(ChangeStatus.Modified, $"docs/page{i}.md")),
            new ChangedPath(ChangeStatus.Added, "src/late.cs"),
            new ChangedPath(ChangeStatus.Added, "src/later.cs"),
        ];

        // Act
        string prompt = BuildPrompt(Facts(paths, issueBody: null));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain($"docs/page{SystemPromptBuilder.MaxReviewPathRows - 1}.md"),
            () => prompt.ShouldNotContain("src/late.cs"),
            () => prompt.ShouldContain("Note: 2 more changed path(s) were omitted from this table."),
            () => prompt.ShouldContain("Production changed, no test changed: yes."));
    }

    [Fact]
    public void WhenAPathHoldsAPipe_EscapesItSoTheTableKeepsItsColumns()
    {
        // Act / Assert
        BuildPrompt(Facts([new ChangedPath(ChangeStatus.Added, "src/a|b.cs")], issueBody: null))
            .ShouldContain("| A | production | src/a\\|b.cs |");
    }

    [Fact]
    public void WhenNoReviewFactsAreSupplied_RendersNoReviewFactsSection()
    {
        // Act / Assert
        BuildPrompt(facts: null).ShouldNotContain("## Review facts");
    }
}
