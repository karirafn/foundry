namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A merge-pending issue as Monitoring's merge step reads it. <paramref name="HeadSha"/> is the head commit the
/// checks gate verified; the merge is pinned to it. <paramref name="FeedbackCutoffAt"/> is where the step reads
/// review feedback from before it merges, so a person's comment stops the merge (issue #917).
/// <paramref name="BranchName"/> is the pull request's source branch, whose ancestry a refused merge reads to tell
/// whether the pull request fell behind (ADR 0111).
/// </summary>
public sealed record MergePendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    string BranchName,
    string HeadSha,
    DateTimeOffset FeedbackCutoffAt,
    DateTimeOffset EnteredMergePendingAt);
