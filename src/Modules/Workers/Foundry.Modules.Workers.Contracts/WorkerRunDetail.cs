namespace Foundry.Modules.Workers.Contracts;

/// <summary>Detail of a single worker run.</summary>
/// <remarks>
/// <c>InputTokens</c> is billable input (uncached + cache-read + cache-write);
/// <see langword="null"/> when any bucket is unknown, e.g. a run that predates cache-column persistence.
/// <para><c>Role</c> is the Worker Role the run was dispatched with (e.g. <c>implement</c>, <c>triage</c>).</para>
/// <para><c>Publish</c> is set on a run that pushed commits; <c>PublishRetry</c> while its state is <c>publish_failed</c>;
/// <c>DiscardsAt</c> while it holds unpublished work (ADR 0100), including a <c>publish_blocked</c> run (#788).</para>
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
    DateTimeOffset? DiscardsAt = null)
{
    /// <summary>The possible secrets that blocked the run's publish (#788); empty unless its state is <c>publish_blocked</c>.</summary>
    public IReadOnlyList<WorkerRunSecretFinding> SecretFindings { get; init; } = [];

    /// <summary>The files the Diff gate reverted before it pushed the run (#788); empty when it reverted none.</summary>
    public IReadOnlyList<WorkerRunRevertedFile> RevertedFiles { get; init; } = [];

    /// <summary>When the Diff gate blocked the run's publish; set only while its state is <c>publish_blocked</c>. A confirmation echoes it.</summary>
    public DateTimeOffset? PublishBlockedAt { get; init; }

    /// <summary>When an operator last confirmed the run's secret findings as false positives (#899); <see langword="null"/> when never.</summary>
    public DateTimeOffset? FalsePositivesConfirmedAt { get; init; }

    /// <summary>Every finding an operator confirmed as a false positive on this run (#899); empty when none.</summary>
    public IReadOnlyList<WorkerRunConfirmedFinding> ConfirmedFindings { get; init; } = [];

    /// <summary>
    /// The toolchain the run's container reported when it started (ADR 0109), in its order; <see langword="null"/> when
    /// the container reported none, or for a run recorded before runs kept it.
    /// </summary>
    public IReadOnlyList<WorkerRunTool>? Toolchain { get; init; }

    /// <summary>
    /// The valid review verdict a review run recorded, with the outcome code derived from it (ADR 0113);
    /// <see langword="null"/> for any other run and for a review run that ended without a valid verdict.
    /// </summary>
    public RecordedReviewVerdict? ReviewVerdict { get; init; }

    /// <summary>
    /// Whether code found that the reviewed change adds or modifies production code while adding or modifying no test
    /// (ADR 0114), independent of the reviewer's own tests assessment; <see langword="null"/> for a run without supplied
    /// review facts.
    /// </summary>
    public bool? ProductionChangedWithoutTests { get; init; }
}

/// <summary>One tool a run's container reported, such as <c>csharp-ls</c> <c>0.28.0</c>.</summary>
public sealed record WorkerRunTool(string Name, string Version);

/// <summary>Where the Diff gate found a possible secret. There is deliberately no member for the matched value.</summary>
public sealed record WorkerRunSecretFinding(string Path, int Line, string CommitSha, string RuleId);

/// <summary>A finding an operator confirmed as a false positive. There is deliberately no member for the matched value.</summary>
public sealed record WorkerRunConfirmedFinding(string Path, int Line, string RuleId);

/// <summary>A file the Diff gate reverted; <c>Reason</c> is <c>oversized</c> or <c>binary</c>.</summary>
public sealed record WorkerRunRevertedFile(string Path, string Reason, long SizeBytes);

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
