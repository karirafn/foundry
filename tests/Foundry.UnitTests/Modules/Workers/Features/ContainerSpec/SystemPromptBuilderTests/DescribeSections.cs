using Foundry.Modules.Workers.Features.ContainerSpec;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class DescribeSections
{
    [Fact]
    public void WhenDescribed_ListsEverySectionInAssemblyOrderWithItsCondition()
    {
        // Act
        IReadOnlyList<PromptSectionDescriptor> sections = SystemPromptBuilder.DescribeSections(timeoutMinutes: 120);

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
            ("input-provided", PromptSectionCondition.Revision),
            ("clarifications", PromptSectionCondition.Clarifications),
        ]);
    }

    [Fact]
    public void WhenDescribed_TheChecksSectionShowsTheTimeoutAndFixPushCapFromSettings()
    {
        // Act
        PromptSectionDescriptor checks = SystemPromptBuilder.DescribeSections(timeoutMinutes: 75)
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
        IReadOnlyList<PromptSectionDescriptor> sections = SystemPromptBuilder.DescribeSections(timeoutMinutes: 120);

        // Assert
        sections.ShouldSatisfyAllConditions(
            () => sections.Single(s => s.Key == "fresh").Content.ShouldContain("<branch-name>{branchName}</branch-name>"),
            () => sections.Single(s => s.Key == "issue-content").Content.ShouldContain("<issue-title>{issueTitle}</issue-title>"),
            () => sections.Single(s => s.Key == "issue-content").Content.ShouldContain("<issue-body>\n{issueBody}\n</issue-body>"),
            () => sections.Single(s => s.Key == "issue-content").Content.ShouldContain("{issueComment}"),
            () => sections.Single(s => s.Key == "issue-content").Content.ShouldNotContain("{issueApiUrl}"),
            () => sections.Single(s => s.Key == "review-feedback").Content.ShouldContain("- {reviewComment}"),
            () => sections.Single(s => s.Key == "failing-checks").Content.ShouldContain("- {checkName} ({conclusion}) {detailsUrl}"),
            () => sections.Single(s => s.Key == "rebase-conflict").Content.ShouldStartWith(SystemPromptBuilder.RebaseConflictSectionMarker),
            () => sections.Single(s => s.Key == "role-prompt").Content.ShouldBe(SystemPromptBuilder.RolePromptSlot));
    }

    [Fact]
    public void WhenDescribed_TheSafetyPreambleIsTheOneEveryPromptStartsWith()
    {
        // Act
        PromptSectionDescriptor preamble = SystemPromptBuilder.DescribeSections(timeoutMinutes: 120)[0];

        // Assert
        preamble.Content.ShouldBe(SystemPromptBuilder.SafetyPreamble);
    }

    [Fact]
    public void WhenDescribed_ThePrinciplesSectionIsASlotForEachRolesOwnPrinciples()
    {
        // Act
        PromptSectionDescriptor principles = SystemPromptBuilder.DescribeSections(timeoutMinutes: 120)
            .Single(section => section.Key == "principles");

        // Assert
        principles.Content.ShouldBe(SystemPromptBuilder.PrinciplesSlot);
    }
}
