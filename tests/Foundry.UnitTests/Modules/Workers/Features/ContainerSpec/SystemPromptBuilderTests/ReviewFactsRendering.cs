using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class ReviewFactsRendering
{
    private const string Template = "You are reviewing issue #{issueNumber}.\n\n{issueContent}\n\nJudge it.";

    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", []);

    private static string BuildPrompt(ReviewFacts? facts) =>
        SystemPromptBuilder.BuildRestricted("Principles.", 42, Template, Issue, Toolchain.Unrecorded, reviewFacts: facts)
            .ShouldBeOfType<Result<string>.Success>().Value;

    [Fact]
    public void WhenNoFactsAreSupplied_RendersNoReviewFactsSection()
    {
        // Act
        string prompt = BuildPrompt(null);

        // Assert
        prompt.ShouldNotContain(SystemPromptBuilder.ReviewFactsSectionMarker);
    }

    [Fact]
    public void RendersEachCriterionByItsId()
    {
        // Arrange
        ReviewFacts facts = ReviewFactsBuilder.With(
            [new ChangedPath(ChangeStatus.Modified, "src/App.cs")],
            AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- [ ] retries <3> times\n- logs it\n"));

        // Act
        string prompt = BuildPrompt(facts);

        // Assert
        prompt.ShouldContain(SystemPromptBuilder.ReviewFactsSectionMarker);
        prompt.ShouldContain("AC1: retries &lt;3&gt; times\nAC2: logs it\n");
    }

    [Fact]
    public void WhenNoCriteriaWereFound_SaysSo()
    {
        // Act
        string prompt = BuildPrompt(ReviewFactsBuilder.With([new ChangedPath(ChangeStatus.Modified, "src/App.cs")]));

        // Assert
        prompt.ShouldContain(SystemPromptBuilder.NoAcceptanceCriteriaNote);
    }

    [Theory]
    [InlineData("src/App.cs", "yes")]
    [InlineData("tests/AppTests.cs", "no")]
    public void RendersTheFlag(string path, string expected)
    {
        // Act
        string prompt = BuildPrompt(ReviewFactsBuilder.With([new ChangedPath(ChangeStatus.Modified, path)]));

        // Assert
        prompt.ShouldContain($"{SystemPromptBuilder.ProductionWithoutTestsLabel} {expected}");
    }

    [Fact]
    public void RendersATableRowPerClassifiedPath()
    {
        // Act
        string prompt = BuildPrompt(ReviewFactsBuilder.With(
            [new ChangedPath(ChangeStatus.Added, "src/A|B.cs"), new ChangedPath(ChangeStatus.Deleted, "tests/ATests.cs")]));

        // Assert
        prompt.ShouldContain("| A | production | src/A\\|B.cs |\n| D | test | tests/ATests.cs |");
    }

    [Fact]
    public void WhenOverTheRowCap_TruncatesTheTableWithACountButComputesTheFlagOverEveryPath()
    {
        // Arrange
        List<ChangedPath> paths = [.. Enumerable.Range(0, SystemPromptBuilder.MaxReviewPathRows)
            .Select(index => new ChangedPath(ChangeStatus.Modified, $"docs/page{index}.md"))];
        paths.Add(new ChangedPath(ChangeStatus.Modified, "src/Late.cs"));
        paths.Add(new ChangedPath(ChangeStatus.Modified, "src/Later.cs"));

        // Act
        string prompt = BuildPrompt(ReviewFactsBuilder.With(paths));

        // Assert
        prompt.ShouldContain($"| M | other | docs/page{SystemPromptBuilder.MaxReviewPathRows - 1}.md |");
        prompt.ShouldNotContain("src/Late.cs");
        prompt.ShouldContain("2 more changed paths are not listed.");
        prompt.ShouldContain($"{SystemPromptBuilder.ProductionWithoutTestsLabel} yes");
    }
}
