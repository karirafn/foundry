namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueDetail(
    Guid Id,
    int IssueNumber,
    string Title,
    string State,
    string RepositorySlug,
    DateTimeOffset DetectedAt,
    string Url,
    string Body,
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
    IReadOnlyList<EligibilityViolationDto>? Violations = null);
