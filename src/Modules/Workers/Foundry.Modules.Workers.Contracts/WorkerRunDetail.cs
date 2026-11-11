namespace Foundry.Modules.Workers.Contracts;

/// <summary>Detail of a single worker run.</summary>
/// <remarks>
/// <c>InputTokens</c> is billable input (uncached + cache-read + cache-write);
/// <see langword="null"/> when any bucket is unknown, e.g. a run that predates cache-column persistence.
/// <para><c>Role</c> is the Worker Role the run was dispatched with (e.g. <c>implement</c>, <c>triage</c>).</para>
/// <para><c>Publish</c> is set on a run that pushed commits; <c>PublishRetry</c> while its state is <c>publish_failed</c>;
/// <c>DiscardsAt</c> while it holds unpublished work (ADR 0100).</para>
/// </remarks>
public sealed record WorkerRunDetail(
    WorkerRunId WorkerRunId,
    Guid IssueId,
    string Role,
    string State,
    string? FailureCategory,
    string? FailureSummary,
    string? ResultText,
    string? Subtype,
    bool? IsError,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    long? InputTokens,
    int? OutputTokens,
    DateTimeOffset? LastActivityAt,
    bool HasStoredLog,
    DateTimeOffset? CancelRequestedAt,
    WorkerRunPublishDetail? Publish = null,
    WorkerRunPublishRetryDetail? PublishRetry = null,
    DateTimeOffset? DiscardsAt = null);

/// <summary>What publishing the run pushed (ADR 0100); provider URLs are built by the API.</summary>
public sealed record WorkerRunPublishDetail(
    string HeadSha,
    string? HeadCommitUrl,
    string? PullRequestUrl,
    int BranchCommitCount);

/// <summary>
/// A run whose publish failed: the attempt it is on (or ended on, once exhausted), when it retries next
/// (<see langword="null"/> once exhausted), and the redacted reason.
/// </summary>
public sealed record WorkerRunPublishRetryDetail(
    int AttemptNumber,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAt,
    string Reason);
