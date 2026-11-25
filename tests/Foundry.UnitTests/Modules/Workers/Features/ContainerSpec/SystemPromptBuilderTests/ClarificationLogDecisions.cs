using System.Text;

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

public sealed class ClarificationLogDecisions
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private const string RestrictedTemplate = "You are triaging GitHub issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", [], SpecHashFixtures.Any);
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly TriageDecision Decision = TriageDecisionBuilder.Build(
        question: "Which <cap>?",
        decision: "Three & no more.",
        ground: TriageDecisionGround.ImplementationDetail,
        evidence: ["src/Retry.cs:8 caps at <three>", "docs/retry.md:2 agrees"]);

    private const string RenderedDecision =
        "<decision ground=\"implementation_detail\">\n<question>Which &lt;cap&gt;?</question>\n<text>Three &amp; no more.</text>\n"
        + "<evidence>src/Retry.cs:8 caps at &lt;three&gt;</evidence>\n<evidence>docs/retry.md:2 agrees</evidence>\n</decision>";

    private static string Build(params Clarification[] log) =>
        SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            new DispatchContext.Fresh("feat/1-fix"),
            Issue,
            timeoutMinutes: 120,
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
            clarifications: log).ShouldBeOfType<Result<string>.Success>().Value;

    private static string BuildRestricted(params Clarification[] log) =>
        SystemPromptBuilder.BuildRestricted(
            Principles,
            1,
            RestrictedTemplate,
            Issue,
            ToolchainFixtures.With(csharpLs: false),
            log).ShouldBeOfType<Result<string>.Success>().Value;

    public static TheoryData<string> PromptKinds() => ["build", "restricted"];

    private static string PromptOf(string kind, params Clarification[] log) =>
        kind == "build" ? Build(log) : BuildRestricted(log);

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void ADecidedRound_RendersItsDecisionsXmlEncodedWithItsDecisionTime(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Clarification.Decide([Decision], At));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain($"<clarification decided-at=\"{At.UtcDateTime:O}\">\n{RenderedDecision}\n</clarification>"),
            () => prompt.ShouldNotContain(SystemPromptBuilder.UnansweredClarificationNote));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void ALogWithDecisions_StatesThatADecisionStandsUnlessALaterCommentOverridesIt(string kind)
    {
        // Act
        string prompt = PromptOf(kind, Clarification.Decide([Decision], At));

        // Assert
        prompt.ShouldContain(SystemPromptBuilder.DecisionStandsRule);
    }

    [Fact]
    public void ALogOfOnlyDecidedRounds_DoesNotSayARunStoppedToAskAPerson()
    {
        // Act
        string prompt = Build(Clarification.Decide([Decision], At));

        // Assert
        prompt.ShouldNotContain("stopped to ask a person");
    }

    [Fact]
    public void ALogWithoutDecisions_DoesNotMentionDecisions()
    {
        // Act
        string prompt = Build(Clarification.Ask([StructuredQuestionBuilder.Build()], At));

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain("Earlier runs stopped to ask a person these questions."),
            () => prompt.ShouldNotContain(SystemPromptBuilder.DecisionStandsRule),
            () => prompt.ShouldNotContain("<decision"));
    }

    [Fact]
    public void AnAskedRoundWithDecisions_RendersItsQuestionsThenItsInputThenItsDecisions()
    {
        // Arrange
        Clarification round = Clarification.Ask([StructuredQuestionBuilder.Build(question: "Which database?")], [Decision], At)
            .Answer(ClarificationInput.FromComments("ok"), At.AddHours(1));

        // Act
        string prompt = Build(round);

        // Assert
        int question = prompt.IndexOf("<question>Which database?</question>", StringComparison.Ordinal);
        int input = prompt.IndexOf("<input>ok</input>", StringComparison.Ordinal);
        int decision = prompt.IndexOf(RenderedDecision, StringComparison.Ordinal);
        prompt.ShouldSatisfyAllConditions(
            () => question.ShouldBeGreaterThan(0),
            () => input.ShouldBeGreaterThan(question),
            () => decision.ShouldBeGreaterThan(input),
            () => prompt.ShouldContain("Earlier runs stopped to ask a person the questions below."),
            () => prompt.ShouldContain(SystemPromptBuilder.DecisionStandsRule));
    }

    [Theory]
    [MemberData(nameof(PromptKinds))]
    public void WhenTheLogMustBeCutToFit_NoDecisionIsCut(string kind)
    {
        // Arrange
        TriageDecision longDecision = TriageDecisionBuilder.Build(decision: new string('d', TriageDecision.DecisionMaxLength));
        string hugeInput = new('x', SystemPromptBuilder.MaxSystemPromptBytes);
        Clarification older = Clarification.Ask([StructuredQuestionBuilder.Build()], [longDecision], At)
            .Answer(ClarificationInput.FromComments(hugeInput), At.AddHours(1));
        Clarification newer = Clarification.Ask([StructuredQuestionBuilder.Build()], At.AddDays(1))
            .Answer(ClarificationInput.FromComments(hugeInput), At.AddDays(1).AddHours(1));

        // Act
        string prompt = PromptOf(kind, older, Clarification.Decide([longDecision], At.AddHours(2)), newer);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => Encoding.UTF8.GetByteCount(prompt).ShouldBeLessThan(SystemPromptBuilder.MaxSystemPromptBytes),
            () => prompt.ShouldContain("[truncated]"),
            () => prompt.Split($"<text>{longDecision.Decision}</text>").Length.ShouldBe(3));
    }
}
