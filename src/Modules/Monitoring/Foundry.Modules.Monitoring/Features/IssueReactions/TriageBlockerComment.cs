using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>The comment Foundry posts on an issue triage held on open issues, naming each blocker and why.</summary>
internal static class TriageBlockerComment
{
    private const char Backtick = '`';

    private const string Opening =
        "Triage found this issue needs these open issues first, and linked them as blockers:";

    private const string Closing =
        "Triage runs again once they close. Remove a link to stop waiting on that issue.";

    public static string Compose(IReadOnlyList<TriageBlocker> blockers) =>
        string.Join(
            '\n',
            [
                Opening,
                .. blockers.Select(blocker => $"- #{blocker.IssueNumber}: {AsCodeSpan(SingleLineText.Cap(blocker.Reason))}"),
                Closing,
            ]);

    // The reason is model output derived from issue content and is posted publicly. Inside a CommonMark code span
    // a mention, link, image or HTML tag stays literal text and cannot notify anyone or swallow later lines.
    private static string AsCodeSpan(string text)
    {
        string fence = new(Backtick, LongestBacktickRun(text) + 1);
        string padding = text.StartsWith(Backtick) || text.EndsWith(Backtick) ? " " : string.Empty;

        return $"{fence}{padding}{text}{padding}{fence}";
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int current = 0;

        foreach (char character in text)
        {
            current = character == Backtick ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}
