using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>The comment Foundry posts on an issue triage held on open issues, naming each blocker and why.</summary>
internal static class TriageBlockerComment
{
    private const string Opening =
        "Triage found this issue needs these open issues first, and linked them as blockers:";

    private const string Closing =
        "Triage runs again once they close. Remove a link to stop waiting on that issue.";

    public static string Compose(IReadOnlyList<TriageBlocker> blockers) =>
        string.Join(
            '\n',
            [
                Opening,
                .. blockers.Select(DescribeBlocker),
                Closing,
            ]);

    private static string DescribeBlocker(TriageBlocker blocker)
    {
        string reason = SingleLineText.Cap(blocker.Reason);

        // The reason is model output derived from issue content and is posted publicly, so it stays literal text.
        return reason.Length > 0 ? $"- #{blocker.IssueNumber}: {ModelMarkdown.CodeSpan(reason)}" : $"- #{blocker.IssueNumber}";
    }
}
