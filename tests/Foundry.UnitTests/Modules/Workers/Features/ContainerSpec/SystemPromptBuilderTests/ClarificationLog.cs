using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.SystemPromptBuilderTests;

public sealed class ClarificationLog
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", []);

    private static readonly DateTimeOffset AskedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly Clarification Answered =
        Clarification.Ask([new InputQuestion("Which database?", "Two are configured.", "Use <sqlite>.")], AskedAt)
            .Answer("Use <postgres>.", AskedAt.AddHours(1));

    public static TheoryData<DispatchContext> Contexts() =>
    [
        new DispatchContext.Fresh("feat/1-fix"),
        new DispatchContext.Continuation("feat/1-fix", string.Empty),
        new DispatchContext.Revision("feat/1-fix", "https://github.com/o/r/pull/1", new RevisionTrigger.InputProvided()),
        new DispatchContext.Revision(
            "feat/1-fix",
            "https://github.com/o/r/pull/1",
            new RevisionTrigger.ReviewFeedback([new ReviewComment("Please fix.")], 0, null)),
    ];

    private static string BuildPrompt(DispatchContext context, IReadOnlyList<Clarification> clarifications) =>
        SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            context,
            Issue,
            timeoutMinutes: 120,
            installsDocker: false,
            installsDotnet: false,
            clarifications: clarifications).ShouldBeOfType<Result<string>.Success>().Value;

    [Theory]
    [MemberData(nameof(Contexts))]
    public void WhenTheLogHasARound_RendersQuestionsAndInputInTheClarificationSection(DispatchContext context)
    {
        // Act
        string prompt = BuildPrompt(context, [Answered]);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.ClarificationSectionMarker),
            () => prompt.ShouldContain("Which database?"),
            () => prompt.ShouldContain("Two are configured."),
            () => prompt.ShouldContain("Use &lt;postgres&gt;."));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void WhenTheLogIsEmpty_ThePromptIsUnchanged(DispatchContext context)
    {
        // Act
        string withEmptyLog = BuildPrompt(context, []);
        string withoutLog = SystemPromptBuilder.Build(
            Principles,
            1,
            Template,
            context,
            Issue,
            timeoutMinutes: 120,
            installsDocker: false,
            installsDotnet: false).ShouldBeOfType<Result<string>.Success>().Value;

        // Assert
        withEmptyLog.ShouldBe(withoutLog);
        withEmptyLog.ShouldNotContain(SystemPromptBuilder.ClarificationSectionMarker);
    }

    [Fact]
    public void WhenARoundIsUnanswered_SaysNoInputArrived()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Fresh("feat/1-fix"),
            [Clarification.Ask([new InputQuestion("Which database?", "Two are configured.", null)], AskedAt)]);

        // Assert
        prompt.ShouldContain(SystemPromptBuilder.UnansweredClarificationNote);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void WhenAQuestionHasARecommendation_RendersItAfterTheQuestionAndContext(DispatchContext context)
    {
        // Act
        string prompt = BuildPrompt(context, [Answered]).ReplaceLineEndings("\n");

        // Assert
        prompt.ShouldContain(
            "<question>Which database?</question>\n<context>Two are configured.</context>\n<recommendation>Use &lt;sqlite&gt;.</recommendation>\n");
    }

    [Fact]
    public void WhenAQuestionHasNoRecommendation_LeavesTheRecommendationOut()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Fresh("feat/1-fix"),
            [Clarification.Ask([new InputQuestion("Which database?", "Two are configured.", null)], AskedAt)]);

        // Assert
        prompt.ShouldNotContain("<recommendation>");
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void TheLeadSentence_SaysAnUnaddressedQuestionTakesItsRecommendation(DispatchContext context)
    {
        // Act
        string prompt = BuildPrompt(context, [Answered]);

        // Assert
        prompt.ShouldContain("A question the input leaves unaddressed takes its recommendation.");
    }

    [Fact]
    public void WhenTheRevisionTriggerIsInputProvided_TellsTheWorkerToApplyTheAnswersOnTheExistingPullRequest()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Revision("feat/1-fix", "https://github.com/o/r/pull/1", new RevisionTrigger.InputProvided()),
            [Answered]);

        // Assert
        prompt.ShouldSatisfyAllConditions(
            () => prompt.ShouldContain(SystemPromptBuilder.InputProvidedSectionMarker),
            () => prompt.ShouldContain("<branch-name>feat/1-fix</branch-name>"));
    }
}
