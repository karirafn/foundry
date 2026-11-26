namespace Foundry.Modules.Issues.Contracts;

/// <param name="CheckOnly">
/// True for a Before Ready rebase that spent its rounds (ADR 0128): the rebase step only reads whether the branch fell
/// behind and never rebases it.
/// </param>
public sealed record RebasePendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset EnteredRebasePendingAt,
    bool CheckOnly);
