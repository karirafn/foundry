using System.Diagnostics;
using System.Text;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Monitoring.Features.AwaitingInput;

/// <summary>The comments Foundry posts to ask an awaiting-input issue's questions and remind of them (ADR 0096), and the link to them.</summary>
internal static class QuestionComment
{
    private const string AcceptRecommendationsClosing =
        "Reply **ok** to accept every recommendation, or answer by number where you choose differently. The work resumes once replies have been quiet for two minutes.";

    // Questions stored before runs returned recommendations have nothing for "ok" to accept.
    private const string ReplyClosing =
        "Reply in a comment on this thread. The work resumes once replies have been quiet for two minutes.";

    private const string StructuredClosing = "_Work resumes two minutes after the last reply._";

    public static string Compose(IReadOnlyList<InputQuestion> questions) =>
        questions.All(question => question is InputQuestion.Legacy)
            ? ComposeLegacy([.. questions.Cast<InputQuestion.Legacy>()])
            : ComposeStructured(questions);

    // The alert's tag line stands alone because GitHub recognises "[!IMPORTANT]" only there, and blank lines surround
    // the <details> content because GitLab renders Markdown inside it only then.
    private static string ComposeStructured(IReadOnlyList<InputQuestion> questions)
    {
        StringBuilder body = new();
        string count = questions.Count == 1 ? "1 question" : $"{questions.Count} questions";
        body.Append("> [!IMPORTANT]\n");
        body.Append("> Foundry needs your input on **").Append(count).Append("** before continuing.\n");
        body.Append("> Reply **ok** to accept all recommendations, or e.g. `1a` to choose differently.\n");

        for (int i = 0; i < questions.Count; i++)
        {
            body.Append('\n');
            body.Append("### ").Append(i + 1).Append(". ").Append(Inline(questions[i].Question)).Append('\n');

            switch (questions[i])
            {
                case InputQuestion.Structured structured:
                    AppendStructured(body, structured);
                    break;
                case InputQuestion.Legacy legacy:
                    AppendLegacy(body, legacy);
                    break;
                default:
                    throw new UnreachableException($"Unknown InputQuestion variant: {questions[i].GetType().Name}");
            }
        }

        body.Append('\n').Append(StructuredClosing);
        return body.ToString();
    }

    private static void AppendStructured(StringBuilder body, InputQuestion.Structured question)
    {
        body.Append("**Problem:** ").Append(Inline(question.Problem)).Append('\n');
        body.Append("**Why it matters:** ").Append(Inline(question.Impact)).Append('\n');
        body.Append('\n');

        for (int index = 0; index < question.Options.Count; index++)
        {
            string letter = InputQuestion.Structured.LetterOf(index);
            string option = Inline(question.Options[index]);

            if (letter == question.RecommendedOption)
            {
                body.Append("- ✅ **").Append(letter).Append(") ").Append(option).Append("**: ")
                    .Append(Inline(question.RecommendationReason)).Append('\n');
            }
            else
            {
                body.Append("- ").Append(letter).Append(") ").Append(option).Append('\n');
            }
        }

        if (question.Evidence.Count == 0)
        {
            return;
        }

        body.Append('\n');
        body.Append("<details><summary>Evidence (").Append(question.Evidence.Count).Append(")</summary>\n");
        body.Append('\n');

        foreach (string evidence in question.Evidence)
        {
            body.Append("- ").Append(Inline(evidence)).Append('\n');
        }

        body.Append('\n');
        body.Append("</details>\n");
    }

    private static void AppendLegacy(StringBuilder body, InputQuestion.Legacy question)
    {
        if (!string.IsNullOrWhiteSpace(question.Context))
        {
            body.Append(Inline(question.Context)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(question.Recommendation))
        {
            body.Append("**Recommended:** ").Append(Inline(question.Recommendation)).Append('\n');
        }
    }

    private static string Inline(string text) => ModelMarkdown.Inline(text.Trim().ReplaceLineEndings(" "));

    private static string ComposeLegacy(IReadOnlyList<InputQuestion.Legacy> questions)
    {
        StringBuilder body = new();
        body.AppendLine("Foundry needs input before the work on this issue can continue.");
        body.AppendLine();

        for (int i = 0; i < questions.Count; i++)
        {
            InputQuestion.Legacy question = questions[i];
            body.Append(i + 1).Append(". **").Append(ModelMarkdown.Inline(question.Question.Trim())).AppendLine("**");

            if (!string.IsNullOrWhiteSpace(question.Context))
            {
                body.Append("   ").AppendLine(ModelMarkdown.Inline(question.Context.Trim().ReplaceLineEndings(" ")));
            }

            if (!string.IsNullOrWhiteSpace(question.Recommendation))
            {
                body.Append("   **Recommended:** ").AppendLine(ModelMarkdown.Inline(question.Recommendation.Trim().ReplaceLineEndings(" ")));
            }
        }

        body.AppendLine();
        body.Append(questions.All(q => !string.IsNullOrWhiteSpace(q.Recommendation))
            ? AcceptRecommendationsClosing
            : ReplyClosing);
        return body.ToString();
    }

    /// <summary>
    /// The one reminder posted in the question's thread once it has waited the reminder delay without input, linking the
    /// question comment when its URL is known.
    /// </summary>
    public static string ComposeReminder(string? questionCommentUrl)
    {
        string questions = questionCommentUrl is null ? "the questions above" : $"[the questions above]({questionCommentUrl})";
        return $"Foundry is still waiting for input on {questions} before the work on this issue can continue. "
            + "Reply in a comment on this thread. The work resumes once replies have been quiet for two minutes.";
    }

    /// <summary>
    /// The link to the posted comment on its thread: GitLab marks notes with <c>#note_</c>, GitHub conversation
    /// comments with <c>#issuecomment-</c>. GitLab web URLs carry the <c>/-/</c> path segment.
    /// </summary>
    public static string LinkTo(string threadUrl, ProviderCommentId commentId) =>
        threadUrl.Contains("/-/", StringComparison.Ordinal)
            ? $"{threadUrl}#note_{commentId}"
            : $"{threadUrl}#issuecomment-{commentId}";
}
