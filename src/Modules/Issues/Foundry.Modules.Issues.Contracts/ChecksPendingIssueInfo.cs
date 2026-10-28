namespace Foundry.Modules.Issues.Contracts;

public sealed record ChecksPendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset EnteredChecksPendingAt,
    DateTimeOffset FeedbackCutoffAt);
