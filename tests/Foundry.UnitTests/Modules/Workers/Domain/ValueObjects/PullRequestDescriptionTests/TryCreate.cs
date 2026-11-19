using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PullRequestDescriptionTests;

public sealed class TryCreate
{
    private static RunPullRequestDescription Draft(
        string[]? changes = null,
        string[]? verification = null,
        string? cause = null,
        string[]? attention = null,
        string? notes = null) =>
        new(cause, changes ?? ["Adds retry."], verification ?? ["dotnet test: 3 passed."], attention ?? [], notes);

    [Fact]
    public void WhenEveryFieldIsWithinItsLimits_KeepsThemAll()
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(
            changes: ["Adds retry.", "Logs each attempt."],
            verification: ["dotnet test: 3 passed."],
            cause: "The first attempt timed out.",
            attention: ["Not run against staging."],
            notes: "## Migration\n\nNone."));

        // Assert
        description.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => description!.Changes.ShouldBe(["Adds retry.", "Logs each attempt."]),
            () => description!.Verification.ShouldBe(["dotnet test: 3 passed."]),
            () => description!.Cause.ShouldBe("The first attempt timed out."),
            () => description!.Attention.ShouldBe(["Not run against staging."]),
            () => description!.Notes.ShouldBe("## Migration\n\nNone."));
    }

    [Theory]
    [InlineData("🤖 Generated with [Claude Code](https://claude.com/claude-code)")]
    [InlineData("Generated with [Claude Code](https://claude.ai/code)")]
    [InlineData("🤖Generated with [Claude Code](https://claude.com/claude-code)  ")]
    public void WhenTheNotesEndWithTheClaudeCodeFooter_RemovesItAndKeepsTheRestIntact(string footer)
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(
            Draft(notes: $"## Summary\n\n- Adds retry.\n- Logs each attempt.\n\n{footer}\n"));

        // Assert
        description.ShouldNotBeNull().Notes.ShouldBe("## Summary\n\n- Adds retry.\n- Logs each attempt.");
    }

    [Fact]
    public void WhenALineMentionsClaudeCodeWithoutBeingTheFooter_KeepsIt()
    {
        // Arrange
        const string notes = "Generated with [Claude Code](https://claude.com/claude-code) and reviewed by hand.";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(notes: notes));

        // Assert
        description.ShouldNotBeNull().Notes.ShouldBe(notes);
    }

    [Theory]
    [InlineData("Co-Authored-By: Jane Doe <jane@example.com>")]
    [InlineData("co-authored-by: Claude <noreply@anthropic.com>")]
    [InlineData("CO-AUTHORED-BY: Claude <noreply@anthropic.com>")]
    public void WhenTheCauseCarriesATrailerInAnyCase_RemovesTheTrailer(string trailer)
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(cause: $"The loop spun.\n\n{trailer}"));

        // Assert
        description.ShouldNotBeNull().Cause.ShouldBe("The loop spun.");
    }

    [Fact]
    public void WhenAListItemIsAttribution_RemovesTheItem()
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(
            changes: ["Adds retry.", "🤖 Generated with [Claude Code](https://claude.com/claude-code)"],
            verification: ["dotnet test: 3 passed.", "Co-Authored-By: Claude <noreply@anthropic.com>"],
            attention: ["co-authored-by: Claude <noreply@anthropic.com>"]));

        // Assert
        description.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => description!.Changes.ShouldBe(["Adds retry."]),
            () => description!.Verification.ShouldBe(["dotnet test: 3 passed."]),
            () => description!.Attention.ShouldBeEmpty());
    }

    [Fact]
    public void WhenAnOptionalFieldIsOnlyAttributionOrBlank_DropsIt()
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(
            cause: " \n\t",
            notes: "\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n"));

        // Assert
        description.ShouldNotBeNull().ShouldSatisfyAllConditions(
            () => description!.Cause.ShouldBeNull(),
            () => description!.Notes.ShouldBeNull());
    }

    [Fact]
    public void WhenNoChangeIsLeftOnceAttributionIsRemoved_ReturnsNull()
    {
        // Act
        PullRequestDescription? description =
            PullRequestDescription.TryCreate(Draft(changes: ["Co-Authored-By: Claude <noreply@anthropic.com>", "  "]));

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public void WhenTheNotesUseCrLfLineEndings_NormalisesThemAndTrimsSurroundingBlankLines()
    {
        // Arrange — two trailing spaces are a Markdown hard line break, so inner lines keep them.
        const string notes = "\r\n\r\nFirst line.  \r\nSecond line.  \r\n\r\n";

        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(Draft(notes: notes));

        // Assert
        description.ShouldNotBeNull().Notes.ShouldBe("First line.  \nSecond line.");
    }

    [Fact]
    public void WhenTheDraftIsMissing_ReturnsNull()
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(null);

        // Assert
        description.ShouldBeNull();
    }

    public static TheoryData<RunPullRequestDescription> DraftsBreakingALimit() =>
    [
        Draft(changes: []),
        Draft(changes: [.. Enumerable.Repeat("Adds retry.", PullRequestDescription.MaxChanges + 1)]),
        Draft(changes: [new string('a', PullRequestDescription.ItemMaxLength + 1)]),
        Draft(changes: ["Adds\nretry."]),
        Draft(verification: []),
        Draft(verification: [.. Enumerable.Repeat("Ran it.", PullRequestDescription.MaxVerification + 1)]),
        Draft(verification: ["Ran\r\nit."]),
        Draft(attention: [.. Enumerable.Repeat("Check it.", PullRequestDescription.MaxAttention + 1)]),
        Draft(attention: [new string('a', PullRequestDescription.AttentionItemMaxLength + 1)]),
        Draft(cause: new string('a', PullRequestDescription.CauseMaxLength + 1)),
        Draft(notes: new string('a', PullRequestDescription.NotesMaxLength + 1)),
    ];

    [Theory]
    [MemberData(nameof(DraftsBreakingALimit))]
    public void WhenAFieldBreaksItsLimit_ReturnsNull(RunPullRequestDescription draft)
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(draft);

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public void LimitsCountCharactersNotUtf16Units()
    {
        // Act
        PullRequestDescription? description = PullRequestDescription.TryCreate(
            Draft(changes: [string.Concat(Enumerable.Repeat("🤖", PullRequestDescription.ItemMaxLength))]));

        // Assert
        description.ShouldNotBeNull();
    }
}
