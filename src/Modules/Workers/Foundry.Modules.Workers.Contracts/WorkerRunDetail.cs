namespace Foundry.Modules.Workers.Contracts;

/// <summary>Detail of a single worker run.</summary>
/// <remarks>
/// <c>InputTokens</c> is billable input (uncached + cache-read + cache-write);
/// <see langword="null"/> when any bucket is unknown, e.g. a run that predates cache-column persistence.
/// </remarks>
public sealed record WorkerRunDetail(
    WorkerRunId WorkerRunId,
    Guid IssueId,
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
    DateTimeOffset? CancelRequestedAt);
