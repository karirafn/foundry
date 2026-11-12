using System.Text;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Monitoring.Features.AwaitingInput;

/// <summary>The comment Foundry posts to ask an awaiting-input issue's questions (ADR 0096), and the link to it.</summary>
internal static class QuestionComment
{
    private const string AcceptRecommendationsClosing =
        "Reply **ok** to accept every recommendation, or answer by number where you choose differently. The work resumes once replies have been quiet for two minutes.";

    // Questions stored before runs returned recommendations have nothing for "ok" to accept.
    private const string ReplyClosing =
        "Reply in a comment on this thread. The work resumes once replies have been quiet for two minutes.";

    public static string Compose(IReadOnlyList<InputQuestion> questions)
    {
        StringBuilder body = new();
        body.AppendLine("Foundry needs input before the work on this issue can continue.");
        body.AppendLine();

        for (int i = 0; i < questions.Count; i++)
        {
            InputQuestion question = questions[i];
            body.Append(i + 1).Append(". **").Append(question.Question.Trim()).AppendLine("**");

            if (!string.IsNullOrWhiteSpace(question.Context))
            {
                body.Append("   ").AppendLine(question.Context.Trim().ReplaceLineEndings(" "));
            }

            if (!string.IsNullOrWhiteSpace(question.Recommendation))
            {
                body.Append("   **Recommended:** ").AppendLine(question.Recommendation.Trim().ReplaceLineEndings(" "));
            }
        }

        body.AppendLine();
        body.Append(questions.All(q => !string.IsNullOrWhiteSpace(q.Recommendation))
            ? AcceptRecommendationsClosing
            : ReplyClosing);
        return body.ToString();
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
