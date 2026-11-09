using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class DescribeSections
{
    private const string Principles = "PRINCIPLES_MARKER Engineering principles.";

    [Fact]
    public void WhenDescribed_ListsEverySectionInAssemblyOrderWithItsCondition()
    {
        // Act
        IReadOnlyList<PromptSectionDescriptor> sections = SystemPromptBuilder.DescribeSections(
            Principles,
            timeoutMinutes: 120);

        // Assert
        sections.Select(section => (section.Key, section.Condition)).ShouldBe(
        [
            ("safety-preamble", PromptSectionCondition.Always),
            ("principles", PromptSectionCondition.Always),
            ("role-prompt", PromptSectionCondition.Always),
            ("issue-content", PromptSectionCondition.Always),
            ("task-list", PromptSectionCondition.Always),
            ("csharp-lsp", PromptSectionCondition.DotnetImage),
            ("pre-pr", PromptSectionCondition.DockerImage),
            ("checks", PromptSectionCondition.Always),
            ("fresh", PromptSectionCondition.Fresh),
            ("continuation", PromptSectionCondition.Continuation),
            ("review-feedback", PromptSectionCondition.Revision),
            ("failing-checks", PromptSectionCondition.Revision),
            ("rebase-conflict", PromptSectionCondition.RebaseConflict),
        ]);
    }

    [Fact]
    public void WhenDescribed_TheChecksSectionShowsTheTimeoutAndFixPushCapFromSettings()
    {
        // Act
        PromptSectionDescriptor checks = SystemPromptBuilder.DescribeSections(Principles, timeoutMinutes: 75)
            .Single(section => section.Key == "checks");

        // Assert
        checks.Content.ShouldSatisfyAllConditions(
            () => checks.Content.ShouldContain("roughly 75 minutes of wall-clock budget"),
            () => checks.Content.ShouldContain($"at most {SystemPromptBuilder.ChecksMaxFixPushes} fix pushes"));
    }

    [Fact]
    public void WhenDescribed_RunSpecificValuesAreLeftAsPlaceholders()
    {
        // Act
        IReadOnlyList<PromptSectionDescriptor> sections = SystemPromptBuilder.DescribeSections(
            Principles,
            timeoutMinutes: 120);

        // Assert
        sections.ShouldSatisfyAllConditions(
            () => sections.Single(s => s.Key == "fresh").Content.ShouldContain("<branch-name>{branchName}</branch-name>"),
            () => sections.Single(s => s.Key == "issue-content").Content.ShouldContain("Issue #{issueNumber}."),
            () => sections.Single(s => s.Key == "review-feedback").Content.ShouldContain("- {reviewComment}"),
            () => sections.Single(s => s.Key == "failing-checks").Content.ShouldContain("- {checkName} ({conclusion}) {detailsUrl}"),
            () => sections.Single(s => s.Key == "rebase-conflict").Content.ShouldStartWith(SystemPromptBuilder.RebaseConflictSectionMarker),
            () => sections.Single(s => s.Key == "role-prompt").Content.ShouldBe(SystemPromptBuilder.RolePromptSlot));
    }

    [Fact]
    public void WhenDescribed_TheSafetyPreambleIsTheOneEveryPromptStartsWith()
    {
        // Act
        PromptSectionDescriptor preamble = SystemPromptBuilder.DescribeSections(Principles, timeoutMinutes: 120)[0];

        // Assert
        preamble.Content.ShouldBe(SystemPromptBuilder.SafetyPreamble);
    }

    [Fact]
    public void WhenDescribed_ThePrinciplesSectionCarriesTheSuppliedText()
    {
        // Act
        PromptSectionDescriptor principles = SystemPromptBuilder.DescribeSections(Principles, timeoutMinutes: 120)
            .Single(section => section.Key == "principles");

        // Assert
        principles.Content.ShouldBe(Principles);
    }
}
