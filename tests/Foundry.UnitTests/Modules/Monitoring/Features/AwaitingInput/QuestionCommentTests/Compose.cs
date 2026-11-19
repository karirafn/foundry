using Foundry.Modules.Monitoring.Features.AwaitingInput;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AwaitingInput.QuestionCommentTests;

public sealed class Compose
{
    private const string AcceptRecommendationsSentence =
        "Reply **ok** to accept every recommendation, or answer by number where you choose differently. The work resumes once replies have been quiet for two minutes.";

    private const string LegacySentence =
        "Reply in a comment on this thread. The work resumes once replies have been quiet for two minutes.";

    [Fact]
    public void WhenEveryQuestionIsLegacy_WritesTodaysText()
    {
        // Act
        string body = QuestionComment.Compose(
            [
                new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres."),
                new InputQuestion.Legacy("Keep v1?", string.Empty, "Yes."),
            ]);

        // Assert
        body.ReplaceLineEndings("\n").ShouldBe(
            "Foundry needs input before the work on this issue can continue.\n\n"
            + "1. **Which database?**\n   Two are configured.\n   **Recommended:** Postgres.\n"
            + "2. **Keep v1?**\n   **Recommended:** Yes.\n\n"
            + AcceptRecommendationsSentence);
    }

    [Fact]
    public void WhenAQuestionHasARecommendation_WritesItAfterTheContextLine()
    {
        // Act
        string body = QuestionComment.Compose(
            [new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres, since production runs it.")]);

        // Assert
        body.ReplaceLineEndings("\n").ShouldContain(
            "1. **Which database?**\n   Two are configured.\n   **Recommended:** Postgres, since production runs it.\n",
            Case.Sensitive);
    }

    [Fact]
    public void WhenTheQuestionsHaveRecommendations_EndsByAskingForOkOrAnswersByNumber()
    {
        // Act
        string body = QuestionComment.Compose(
            [
                new InputQuestion.Legacy("Which database?", "Two are configured.", "Postgres."),
                new InputQuestion.Legacy("Keep v1?", string.Empty, "Yes, clients still call it."),
            ]);

        // Assert
        body.ShouldEndWith(AcceptRecommendationsSentence);
    }

    [Fact]
    public void WhenNoQuestionHasARecommendation_WritesNoRecommendedLineAndEndsWithTheLegacySentence()
    {
        // Act
        string body = QuestionComment.Compose([new InputQuestion.Legacy("Which database?", "Two are configured.", null)]);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldNotContain("Recommended:"),
            () => body.ShouldEndWith(LegacySentence));
    }

    [Fact]
    public void WhenModelTextMentionsSomeone_TheMentionIsNeutralizedInEveryField()
    {
        // Act
        string body = QuestionComment.Compose(
            [new InputQuestion.Legacy("Ask @alice?", "@org/team owns it.", "Ping @bob.")]);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("Ask @\u200Balice?"),
            () => body.ShouldContain("@\u200Borg/team owns it."),
            () => body.ShouldContain("Ping @\u200Bbob."));
    }

    [Fact]
    public void WhenModelTextHoldsAnHtmlTag_TheTagIsWrittenAsText()
    {
        // Act
        string body = QuestionComment.Compose(
            [new InputQuestion.Legacy("Close </details>?", "<img src=x>", "Keep <b>.")]);

        // Assert
        body.ShouldSatisfyAllConditions(
            () => body.ShouldContain("Close &lt;/details&gt;?"),
            () => body.ShouldContain("&lt;img src=x&gt;"),
            () => body.ShouldContain("Keep &lt;b&gt;."),
            () => body.ShouldNotContain("<"));
    }
}
