namespace Foundry.Modules.Workers.Contracts;

public sealed record WorkerRunDetail(
    Guid WorkerRunId,
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
    int? InputTokens,
    int? OutputTokens,
    DateTimeOffset? LastActivityAt,
    IReadOnlyList<WorkerRunCommitMarker> CommitMarkers,
    bool HasStoredLog);

public sealed record WorkerRunCommitMarker(
    DateTimeOffset ObservedAt,
    string Sha,
    string Message);
