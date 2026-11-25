using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AwaitingInput.QuestionCommentTests;

public sealed class ComposeStructured
{
    [Fact]
    public void WhenOneQuestionIsAsked_LeadsWithOneAlertAndLaysOutTheQuestion()
    {
        // Arrange
        InputQuestion.Structured question = StructuredQuestionBuilder.Build(
            question: "Which database?",
            problem: "Two are configured.",
            impact: "Migrations target the wrong one.",
            options: ["SQLite", "Postgres", "Both"],
            recommendedOption: "b",
            recommendationReason: "Production runs it.",
            evidence: ["src/Db.cs:12 opens SQLite", "appsettings.json:4 names Postgres"]);

        // Act
        string body = QuestionComment.Compose([question], []);

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
            - c) Both

            <details><summary>Evidence (2)</summary>

            - src/Db.cs:12 opens SQLite
            - appsettings.json:4 names Postgres

            </details>

            _Work resumes two minutes after the last reply._
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WhenThreeQuestionsAreAsked_CountsThemAndNumbersEachHeading()
    {
        // Act
        string body = QuestionComment.Compose(
            [
                StructuredQuestionBuilder.Build(question: "First?"),
                StructuredQuestionBuilder.Build(question: "Second?"),
                StructuredQuestionBuilder.Build(question: "Third?"),
            ],
            []);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("Foundry needs your input on **3 questions** before continuing."),
            () => body.ShouldContain("### 1. First?\n"),
            () => body.ShouldContain("### 2. Second?\n"),
            () => body.ShouldContain("### 3. Third?\n"));
    }

    [Fact]
    public void WhenTheFirstOptionIsRecommended_MarksOnlyTheFirstOption()
    {
        // Act
        string body = QuestionComment.Compose(
            [StructuredQuestionBuilder.Build(options: ["One", "Two", "Three"], recommendedOption: "a", recommendationReason: "Simplest.")], []);

        // Assert
        body.ShouldContain("- ✅ **a) One**: Simplest.\n- b) Two\n- c) Three\n");
    }

    [Fact]
    public void WhenTheLastOptionIsRecommended_MarksOnlyTheLastOption()
    {
        // Act
        string body = QuestionComment.Compose(
            [StructuredQuestionBuilder.Build(options: ["One", "Two", "Three", "Four"], recommendedOption: "d", recommendationReason: "Safest.")], []);

        // Assert
        body.ShouldContain("- a) One\n- b) Two\n- c) Three\n- ✅ **d) Four**: Safest.\n");
    }

    [Fact]
    public void WhenEvidenceIsEmpty_OmitsTheEvidenceSection()
    {
        // Act
        string body = QuestionComment.Compose([StructuredQuestionBuilder.Build(evidence: [])], []);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldNotContain("<details>"),
            () => body.ShouldNotContain("Evidence"));
    }

    [Fact]
    public void WhenModelTextHoldsAMentionAndAClosingDetailsTag_NeutralizesBoth()
    {
        // Act
        string body = QuestionComment.Compose(
            [StructuredQuestionBuilder.Build(
                question: "Ask @alice about </details>?",
                problem: "@org/team owns </details>.",
                options: ["Ping @bob", "Close </details>"],
                evidence: ["src/x.cs:1 says </details> @carol"])], []);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("### 1. Ask @\u200Balice about &lt;/details&gt;?"),
            () => body.ShouldContain("**Problem:** @\u200Borg/team owns &lt;/details&gt;."),
            () => body.ShouldContain("- a) Ping @\u200Bbob"),
            () => body.ShouldContain("**b) Close &lt;/details&gt;**"),
            () => body.ShouldContain("- src/x.cs:1 says &lt;/details&gt; @\u200Bcarol"),
            () => body.Split("</details>").Length.ShouldBe(2));
    }
}
