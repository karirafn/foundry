using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AwaitingInput.QuestionCommentTests;

public sealed class ComposeWithDecisions
{
    private const string ShortenedNote = "_Decision text is shortened to fit one comment._";

    [Fact]
    public void WhenTheRoundCarriesDecisions_ListsThemAfterTheQuestionsAndBeforeTheClosingLine()
    {
        // Arrange
        InputQuestion.Structured question = StructuredQuestionBuilder.Build(
            question: "Which database?",
            problem: "Two are configured.",
            impact: "Migrations target the wrong one.",
            options: ["SQLite", "Postgres"],
            recommendedOption: "b",
            recommendationReason: "Production runs it.",
            evidence: []);
        TriageDecision[] decisions =
        [
            TriageDecisionBuilder.Build(question: "Which retry cap?", decision: "Three attempts."),
            TriageDecisionBuilder.Build(
                question: "Which log level?", decision: "Warning.", ground: TriageDecisionGround.ImplementationDetail),
        ];

        // Act
        string body = QuestionComment.Compose([question], decisions);

        // Assert
        body.ReplaceLineEndings("\n").ShouldBe(
            """
            > [!IMPORTANT]
            > Foundry needs your input on **1 question** before continuing.
            > Reply **ok** to accept all recommendations, or e.g. `1a` to choose differently.

            ### 1. Which database?
            **Problem:** Two are configured.
            **Why it matters:** Migrations target the wrong one.

            - a) SQLite
            - ✅ **b) Postgres**: Production runs it.

            Triage also decided:
            - `Which retry cap?`: `Three attempts.` (Settled by existing behaviour)
            - `Which log level?`: `Warning.` (Implementation detail)

            _Work resumes two minutes after the last reply._
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WhenEveryQuestionIsLegacyAndTheRoundCarriesDecisions_ListsThemBeforeTheClosingSentence()
    {
        // Act
        string body = QuestionComment.Compose(
            [new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres.")],
            [TriageDecisionBuilder.Build(question: "Which retry cap?", decision: "Three attempts.")]);

        // Assert
        body.ReplaceLineEndings("\n").ShouldEndWith(
            """
               **Recommended:** Postgres.

            Triage also decided:
            - `Which retry cap?`: `Three attempts.` (Settled by existing behaviour)

            Reply **ok** to accept every recommendation, or answer by number where you choose differently. The work resumes once replies have been quiet for two minutes.
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WhenTheRoundCarriesNoDecisions_HasNoDecisionSection()
    {
        // Act
        string body = QuestionComment.Compose([StructuredQuestionBuilder.Build()], []);

        // Assert
        body.ShouldNotContain("Triage also decided:");
    }

    [Fact]
    public void WhenOverTheProviderLimit_ShortensDecisionTextButNotQuestionLines()
    {
        // Arrange
        InputQuestion.Structured question = StructuredQuestionBuilder.Build(question: "Which database?");
        TriageDecision[] decisions =
        [
            .. Enumerable.Range(1, 20).Select(index => TriageDecisionBuilder.Build(
                question: $"Question {index} {new string('q', 100)}",
                decision: new string('`', TriageDecision.DecisionMaxLength))),
        ];

        // Act
        string body = QuestionComment.Compose([question], decisions);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.Length.ShouldBeLessThanOrEqualTo(60_000),
            () => body.ShouldContain("### 1. Which database?"),
            () => body.ShouldContain(ShortenedNote),
            () => decisions.ShouldAllBe(decision => body.Contains($"`{decision.Question}`")),
            () => body.ShouldEndWith("_Work resumes two minutes after the last reply._"));
    }
}
