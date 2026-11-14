namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A merge-pending issue as Monitoring's merge step reads it. <paramref name="HeadSha"/> is the head commit the
/// checks gate verified; the merge is pinned to it. <paramref name="FeedbackCutoffAt"/> is where the step reads
/// review feedback from before it merges, so a person's comment stops the merge (issue #917).
/// </summary>
public sealed record MergePendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    string HeadSha,
    DateTimeOffset FeedbackCutoffAt,
    DateTimeOffset EnteredMergePendingAt);
