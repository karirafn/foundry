namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for an issue, as presented by the Issues module.
/// </summary>
public sealed record RunStats(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    long? InputTokens,
    long? OutputTokens);
