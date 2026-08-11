namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueDetail(
    Guid Id,
    int IssueNumber,
    string Title,
    string State,
    string RepositorySlug,
    string ProviderType,
    DateTimeOffset DetectedAt,
    string Url,
    string Author,
    IReadOnlyList<string> Labels,
    IssueStateDetails? StateDetails);

public sealed record IssueStateDetails(
    Guid? WorkerRunId,
    string? BranchName,
    string? PullRequestUrl,
    DateTimeOffset? FeedbackCutoffAt,
    string? FailureReason,
    DateTimeOffset? FailedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<int>? BlockedBy,
    TransientRetryDetails? TransientRetry);

public sealed record TransientRetryDetails(
    int AttemptNumber,
    int MaxAttempts,
    bool IsExhausted,
    DateTimeOffset? NextAttemptDueAt);
