using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs for an issue, as presented by the Issues module.
/// </summary>
/// <remarks>
/// <see cref="InputTokens"/> is billable input (uncached + cache-read + cache-write).
/// <see cref="InputTokens"/> and <see cref="OutputTokens"/> are partial sums with known/unknown run
/// counts per metric (see <see cref="TokenTotal"/>); a partial total renders as a lower bound.
/// </remarks>
public sealed record RunStats(
    int RunCount,
    long? DurationMs,
    int? NumTurns,
    decimal? TotalCostUsd,
    TokenTotal InputTokens,
    TokenTotal OutputTokens);
