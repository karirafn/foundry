namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for a single issue.
/// </summary>
/// <remarks>
/// <see cref="InputTokens"/> is billable input (uncached + cache-read + cache-write).
/// <see cref="InputTokens"/> and <see cref="OutputTokens"/> are partial sums over the runs with a
/// result summary whose figure is known, with known/unknown run counts per metric (see
/// <see cref="TokenTotal"/>). Runs without a result summary count as neither known nor unknown.
/// </remarks>
public sealed record RunAggregate(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    TokenTotal InputTokens,
    TokenTotal OutputTokens);
