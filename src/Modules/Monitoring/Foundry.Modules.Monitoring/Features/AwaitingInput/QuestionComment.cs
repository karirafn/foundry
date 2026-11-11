using System.Text;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Monitoring.Features.AwaitingInput;

/// <summary>The comment Foundry posts to ask an awaiting-input issue's questions (ADR 0096), and the link to it.</summary>
internal static class QuestionComment
{
    public static string Compose(IReadOnlyList<InputQuestion> questions)
    {
        StringBuilder body = new();
        body.AppendLine("Foundry needs input before the work on this issue can continue.");
        body.AppendLine();

        for (int i = 0; i < questions.Count; i++)
        {
            body.Append(i + 1).Append(". **").Append(questions[i].Question.Trim()).AppendLine("**");

            if (!string.IsNullOrWhiteSpace(questions[i].Context))
            {
                body.Append("   ").AppendLine(questions[i].Context.Trim().ReplaceLineEndings(" "));
            }
        }

        body.AppendLine();
        body.Append("Reply in a comment on this thread. The work resumes once replies have been quiet for two minutes.");
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
