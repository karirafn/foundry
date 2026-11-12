using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>The comment Foundry posts on an issue triage held on open issues, naming each blocker and why.</summary>
internal static class TriageBlockerComment
{
    private const int MaxReasonLength = 500;

    private const string Ellipsis = "…";

    private const string Opening =
        "Triage found this issue needs these open issues first, and linked them as blockers:";

    private const string Closing =
        "Triage runs again once they close. Remove a link to stop waiting on that issue.";

    public static string Compose(IReadOnlyList<TriageBlocker> blockers) =>
        string.Join(
            '\n',
            [Opening, .. blockers.Select(blocker => $"- #{blocker.IssueNumber}: {Flatten(blocker.Reason)}"), Closing]);

    // The reason is model output derived from issue content and is posted publicly, so it stays on its own
    // line and cannot forge further list entries.
    private static string Flatten(string reason)
    {
        string flattened = reason.Trim().ReplaceLineEndings(" ");
        if (flattened.Length <= MaxReasonLength)
        {
            return flattened;
        }

        int keep = MaxReasonLength - Ellipsis.Length;
        if (char.IsHighSurrogate(flattened[keep - 1]))
        {
            keep--;
        }

        return string.Concat(flattened.AsSpan(0, keep), Ellipsis);
    }
}
