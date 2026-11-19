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

public sealed class ClarificationLog
{
    private const string Principles = "Engineering principles.";
    private const string Template = "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}";
    private static readonly IssueContent Issue = new("Add retry", "Retry webhook delivery.", []);

    private static readonly DateTimeOffset AskedAt = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly Clarification Answered =
        Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", "Use <sqlite>.")], AskedAt)
            .Answer(ClarificationInput.FromComments("Use <postgres>."), AskedAt.AddHours(1));

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
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false),
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
            toolchain: ToolchainFixtures.With(docker: false, csharpLs: false)).ShouldBeOfType<Result<string>.Success>().Value;

        // Assert
        withEmptyLog.ShouldBe(withoutLog);
        withEmptyLog.ShouldNotContain(SystemPromptBuilder.ClarificationSectionMarker);
    }

    [Fact]
    public void WhenARoundWasAnsweredByAnEdit_SaysTheDescriptionWasEditedAfterTheCommentText()
    {
        // Arrange
        Clarification answeredByEdit =
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(
                    new ClarificationInput([new InputPart.Comments("See the description."), new InputPart.DescriptionEdited()]),
                    AskedAt.AddHours(1));

        // Act
        string prompt = BuildPrompt(new DispatchContext.Fresh("feat/1-fix"), [answeredByEdit]);

        // Assert
        prompt.ShouldContain("See the description.\n\nThe issue description was edited after the question was posted.");
    }

    [Fact]
    public void WhenARoundWasAnsweredByAPush_TellsTheRunToReviewThePushedCommits()
    {
        // Arrange
        Clarification answeredByPush =
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.BranchUpdated("aaa111", "bbb222")]), AskedAt.AddHours(1));

        // Act
        string prompt = BuildPrompt(new DispatchContext.Continuation("feat/1-fix"), [answeredByPush]);

        // Assert
        prompt.ShouldContain(
            "Someone pushed to the branch after the question was posted: aaa111..bbb222. Review those commits before continuing.");
    }

    [Fact]
    public void WhenARoundWasAnsweredByRecreatingTheBranch_SaysWhereItWasRecreated()
    {
        // Arrange
        Clarification answeredByRecreation =
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.BranchUpdated(null, "bbb222")]), AskedAt.AddHours(1));

        // Act
        string prompt = BuildPrompt(new DispatchContext.Continuation("feat/1-fix"), [answeredByRecreation]);

        // Assert
        prompt.ShouldContain("The branch was recreated at bbb222 after the question was posted.");
    }

    [Fact]
    public void WhenARoundWasContinuedWithoutAnswer_TellsTheRunToUseItsJudgment()
    {
        // Arrange
        Clarification continued =
            Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)
                .Answer(new ClarificationInput([new InputPart.ContinuedWithoutAnswer()]), AskedAt.AddDays(3));

        // Act
        string prompt = BuildPrompt(new DispatchContext.Fresh("feat/1-fix"), [continued]);

        // Assert
        prompt.ShouldContain("No answer arrived; the operator chose to continue. Use your judgment.");
    }

    [Fact]
    public void WhenARoundIsUnanswered_SaysNoInputArrived()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Fresh("feat/1-fix"),
            [Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)]);

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
    public void WhenAQuestionIsStructured_RendersItsLetteredOptionsRecommendationAndEvidence()
    {
        // Arrange
        Clarification round = Clarification.Ask(
            [StructuredQuestionBuilder.Build(
                question: "Which <database>?",
                problem: "Two are configured.",
                impact: "Migrations target the wrong one.",
                options: ["SQLite", "Postgres & co"],
                recommendedOption: "b",
                recommendationReason: "Production runs it.",
                evidence: ["src/Db.cs:12 opens SQLite"])],
            AskedAt);

        // Act
        string prompt = BuildPrompt(new DispatchContext.Fresh("feat/1-fix"), [round]).ReplaceLineEndings("\n");

        // Assert
        prompt.ShouldContain(
            "<question>Which &lt;database&gt;?</question>\n"
            + "<problem>Two are configured.</problem>\n"
            + "<impact>Migrations target the wrong one.</impact>\n"
            + "<option letter=\"a\">SQLite</option>\n"
            + "<option letter=\"b\">Postgres &amp; co</option>\n"
            + "<recommendation option=\"b\">Production runs it.</recommendation>\n"
            + "<evidence>src/Db.cs:12 opens SQLite</evidence>\n");
    }

    [Fact]
    public void TheLeadSentence_SaysALetteredReplyPicksAnOption()
    {
        // Act
        string prompt = BuildPrompt(new DispatchContext.Fresh("feat/1-fix"), [Answered]);

        // Assert
        prompt.ShouldContain("A reply such as `1b` picks option b of question 1.");
    }

    [Fact]
    public void WhenAQuestionHasNoRecommendation_LeavesTheRecommendationOut()
    {
        // Act
        string prompt = BuildPrompt(
            new DispatchContext.Fresh("feat/1-fix"),
            [Clarification.Ask([new InputQuestion.Legacy("Which database?", "Two are configured.", null)], AskedAt)]);

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
