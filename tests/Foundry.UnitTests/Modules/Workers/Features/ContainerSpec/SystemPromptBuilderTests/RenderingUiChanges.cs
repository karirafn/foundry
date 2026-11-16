using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class RenderingUiChanges
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Fix the layout", "The cards have unequal heights.", []);

    [Fact]
    public void WhenTheToolchainHasChromiumAndPlaywrightCore_IncludesTheSection()
    {
        // Act
        string prompt = BuildPrompt(ToolchainFixtures.With(chromium: true, playwrightCore: true));

        // Assert
        prompt.ShouldContain(SystemPromptBuilder.RenderingUiChangesSectionMarker);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void WhenTheToolchainLacksEitherTool_OmitsTheSection(bool chromium, bool playwrightCore)
    {
        // Act
        string prompt = BuildPrompt(ToolchainFixtures.With(chromium: chromium, playwrightCore: playwrightCore));

        // Assert
        prompt.ShouldNotContain(SystemPromptBuilder.RenderingUiChangesSectionMarker);
    }

    [Fact]
    public void WhenTheToolchainIsUnrecorded_OmitsTheSection()
    {
        // Act
        string prompt = BuildPrompt(Toolchain.Unrecorded);

        // Assert
        prompt.ShouldNotContain(SystemPromptBuilder.RenderingUiChangesSectionMarker);
    }

    [Fact]
    public void WhenIncluded_TellsTheAgentWhenAndHowToRenderAndMeasure()
    {
        // Act
        string prompt = BuildPrompt(ToolchainFixtures.With(chromium: true, playwrightCore: true));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("After changing a stylesheet, template or component layout"),
            () => prompt.ShouldContain("`getBoundingClientRect`"),
            () => prompt.ShouldContain("narrowest and widest widths the layout supports"),
            () => prompt.ShouldContain("empty, typical and extreme content"),
            () => prompt.ShouldContain("its own browser layout tests, run and extend those instead"),
            () => prompt.ShouldContain("require('/opt/claude-code/node_modules/playwright-core')"),
            () => prompt.ShouldContain(
                "import { chromium } from '/opt/claude-code/node_modules/playwright-core/index.mjs'"),
            () => prompt.ShouldContain("ES module resolution ignores `NODE_PATH`"),
            () => prompt.ShouldContain("chromium.launch({ executablePath: process.env.CHROME_BIN })"),
            () => prompt.ShouldContain("Render only local content"),
            () => prompt.ShouldContain("under `/tmp`, outside the repository"));
    }

    [Fact]
    public void WhenIncluded_PrecedesThePublishingSection()
    {
        // Act
        string prompt = BuildPrompt(ToolchainFixtures.With(docker: true, chromium: true, playwrightCore: true));

        // Assert
        prompt.IndexOf(SystemPromptBuilder.RenderingUiChangesSectionMarker, StringComparison.Ordinal)
            .ShouldBeLessThan(prompt.IndexOf(SystemPromptBuilder.PublishingSectionMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void WhenTheRunIsRestricted_OmitsTheSectionEvenWithBothTools()
    {
        // Act
        string prompt = SystemPromptBuilder.BuildRestricted(
                Principles,
                42,
                Template,
                Issue,
                ToolchainFixtures.With(chromium: true, playwrightCore: true))
            .ShouldBeOfType<Result<string>.Success>().Value;

        // Assert
        prompt.ShouldNotContain(SystemPromptBuilder.RenderingUiChangesSectionMarker);
    }

    private static string BuildPrompt(Toolchain toolchain) =>
        SystemPromptBuilder.Build(
                Principles,
                42,
                Template,
                new DispatchContext.Fresh("feat/42-fix-the-layout"),
                Issue,
                timeoutMinutes: 120,
                toolchain: toolchain)
            .ShouldBeOfType<Result<string>.Success>().Value;
}
