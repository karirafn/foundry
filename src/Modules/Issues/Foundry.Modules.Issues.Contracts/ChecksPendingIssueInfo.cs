namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// A checks-pending issue as the checks gate reads it. <paramref name="OwedChecksRetryHeadSha"/> is the head commit
/// a red verdict retries the failing CI jobs on, or <see langword="null"/> when no retry is owed.
/// <paramref name="ChecksRetriedAt"/> is set while retried jobs have not been read pending yet.
/// </summary>
public sealed record ChecksPendingIssueInfo(
    int IssueNumber,
    string PullRequestUrl,
    DateTimeOffset EnteredChecksPendingAt,
    DateTimeOffset FeedbackCutoffAt,
    string? OwedChecksRetryHeadSha = null,
    DateTimeOffset? ChecksRetriedAt = null);
