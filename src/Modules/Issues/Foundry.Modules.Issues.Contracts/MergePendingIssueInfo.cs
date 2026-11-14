namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A merge-pending issue as Monitoring's merge step reads it. <paramref name="HeadSha"/> is the head commit the
/// checks gate verified; the merge is pinned to it.
/// </summary>
public sealed record MergePendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    string HeadSha,
    DateTimeOffset EnteredMergePendingAt);
