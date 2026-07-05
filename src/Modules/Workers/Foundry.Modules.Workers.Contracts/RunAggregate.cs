namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for a single issue.
/// </summary>
public sealed record RunAggregate(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    long? InputTokens,
    long? OutputTokens);
