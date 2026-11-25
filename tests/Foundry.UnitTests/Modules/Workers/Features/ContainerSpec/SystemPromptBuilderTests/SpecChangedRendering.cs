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

public sealed class SpecChangedRendering
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any);

    private static string BuildPrompt(DispatchContext context) =>
        SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            context,
            Issue,
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false)).ShouldBeOfType<Result<string>.Success>().Value;

    [Fact]
    public void WhenTheRevisionTriggerIsSpecChanged_TellsTheWorkerTheDescriptionWasRewrittenSinceTheLastTriage()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Revision("feat/1-fix", "https://github.com/o/r/pull/1", new RevisionTrigger.SpecChanged()));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.SpecChangedSectionMarker),
            () => prompt.ShouldContain("The issue description was rewritten since the last triage."),
            () => prompt.ShouldContain("bring the branch in line with the issue as it now reads"),
            () => prompt.ShouldContain("<branch-name>feat/1-fix</branch-name>"));
    }

    [Fact]
    public void WhenTheRevisionTriggerIsSpecChanged_RendersNoOtherRevisionSection()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Revision("feat/1-fix", "https://github.com/o/r/pull/1", new RevisionTrigger.SpecChanged()));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldNotContain(SystemPromptBuilder.InputProvidedSectionMarker),
            () => prompt.ShouldNotContain(SystemPromptBuilder.RebaseConflictSectionMarker));
    }
}
