namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for a single issue.
/// </summary>
/// <remarks>
/// <see cref="InputTokens"/> is billable input (uncached + cache-read + cache-write).
/// <see cref="InputTokens"/> and <see cref="OutputTokens"/> are <see langword="null"/> when any
/// run with a result summary has an unknown figure, or when no run has one.
/// </remarks>
public sealed record RunAggregate(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    long? InputTokens,
    long? OutputTokens);
