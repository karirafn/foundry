namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A review issue as the review pass reads it. <paramref name="VerifiedHeadSha"/> is the head commit its checks
/// verdict was read against, or null when it is unknown (the pass then re-checks the issue).
/// </summary>
public sealed record ReviewIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset FeedbackCutoffAt,
    string? VerifiedHeadSha);
