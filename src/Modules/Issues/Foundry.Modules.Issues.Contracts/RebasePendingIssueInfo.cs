namespace Foundry.Modules.Issues.Contracts;

public sealed record RebasePendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset EnteredRebasePendingAt);
