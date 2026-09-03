namespace Foundry.Modules.Workers.Contracts;

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
    int? InputTokens,
    int? OutputTokens,
    DateTimeOffset? LastActivityAt,
    bool HasStoredLog);
