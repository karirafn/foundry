namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A review issue as the review pass reads it. <paramref name="VerifiedHeadSha"/> is the head commit its checks
/// verdict was read against, or null when it is unknown (the pass then re-checks the issue).
/// <paramref name="ChecksPassed"/> is whether the stored checks verdict is passed; any other verdict has its checks
/// read again on an unchanged head, because a rerun of the same commits may since have turned green (ADR 0108).
/// </summary>
public sealed record ReviewIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset FeedbackCutoffAt,
    string? VerifiedHeadSha,
    bool ChecksPassed);
