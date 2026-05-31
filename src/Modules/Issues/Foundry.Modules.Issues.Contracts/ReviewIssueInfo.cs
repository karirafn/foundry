namespace Foundry.Modules.Issues.Contracts;

public sealed record ReviewIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset FeedbackCutoffAt);
