using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class DecisionRuleRendering
{
    private const string Principles = "Engineering principles.\n\nDesign:\n- [design.1] Safe defaults.";
    private const string Rule = "Ask about uncertain business logic.\nDecide the rest.";
    private const string RoleLine = "You are working on issue #{issueNumber}.";
    private const string Template = RoleLine + "\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any);

    public static TheoryData<string> PromptKinds() => ["build", "restricted"];

    private static string PromptOf(string kind, string decisionRule) =>
        kind == "build"
            ? SystemPromptBuilder.Build(
                Principles,
                1,
                Template,
                new DispatchContext.Fresh("feat/1-fix"),
                Issue,
                timeoutMinutes: 120,
                toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
                decisionRule: decisionRule).ShouldBeOfType<Result<string>.Success>().Value
            : SystemPromptBuilder.BuildRestricted(
                Principles,
                1,
                Template,
                Issue,
                ToolchainFixtures.With(csharpLs: false),
                decisionRule: decisionRule).ShouldBeOfType<Result<string>.Success>().Value;

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheRoleCarriesTheDecisionRule_RendersItOnceUnderItsOwnHeading(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Rule);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.Split(Rule).Length.ShouldBe(2),
            () => prompt.ShouldContain(SystemPromptBuilder.DecisionRuleHeading + "\n\n" + Rule));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheRoleCarriesTheDecisionRule_PlacesItAfterThePrinciplesAndBeforeTheRolePrompt(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Rule);

        // Assert
        int rule = prompt.IndexOf(SystemPromptBuilder.DecisionRuleHeading, StringComparison.Ordinal);
        prompt.ShouldSatisfyAllConditions(
            () => prompt.IndexOf(Principles, StringComparison.Ordinal).ShouldBeLessThan(rule),
            () => rule.ShouldBeLessThan(prompt.IndexOf("You are working on issue #1.", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheRoleDoesNotCarryTheDecisionRule_RendersNoHeading(string kind)
    {
        // Act
        string prompt = PromptOf(kind, string.Empty);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldNotContain(SystemPromptBuilder.DecisionRuleHeading),
            () => prompt.ShouldContain(Principles + "\n\nYou are working on issue #1."));
    }
}
