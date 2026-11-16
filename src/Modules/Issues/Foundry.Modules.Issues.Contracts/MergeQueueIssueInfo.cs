namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// An issue whose pull request Foundry may merge, as Monitoring's catch-up queue reads it (ADR 0111): every
/// <c>checks_pending</c> and <c>merge_pending</c> issue, and every <c>rebase_pending</c> issue rebasing for a
/// catch-up. <paramref name="IsCatchingUp"/> is set while the issue rebases or waits on checks for a catch-up, so its
/// queue entry keeps its place. <paramref name="HasPersonFeedback"/> is set when a person's review feedback drove the
/// issue's revisions; such an issue goes to review instead of merging.
/// </summary>
public sealed record MergeQueueIssueInfo(
    int IssueNumber,
    string BranchName,
    bool IsCatchingUp,
    bool HasPersonFeedback);
