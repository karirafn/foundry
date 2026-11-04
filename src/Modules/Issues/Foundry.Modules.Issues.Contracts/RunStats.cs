namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for an issue, as presented by the Issues module.
/// </summary>
/// <remarks>
/// <see cref="InputTokens"/> is billable input (uncached + cache-read + cache-write);
/// <see langword="null"/> renders as unknown when any run's figure is unknown.
/// </remarks>
public sealed record RunStats(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    long? InputTokens,
    long? OutputTokens);
