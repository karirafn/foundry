namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs in a time window.
/// Each field is zero when no completed runs contribute to that dimension.
/// </summary>
/// <remarks>
/// In-progress runs (<c>StartingRun</c> and <c>ActiveRun</c>) contribute to
/// <see cref="RunCount"/> but contribute nothing to <see cref="DurationMs"/>,
/// <see cref="NumTurns"/>, <see cref="TotalCostUsd"/>, <see cref="InputTokens"/>,
/// or <see cref="OutputTokens"/>, because telemetry is only available once a run completes.
/// <para>
/// <see cref="InputTokens"/> is billable input (uncached + cache-read + cache-write).
/// <see cref="InputTokens"/> and <see cref="OutputTokens"/> are partial sums over the runs whose
/// figure is known, with known/unknown run counts per metric (see <see cref="TokenTotal"/>). A run
/// that predates cache-column persistence is unknown for input but known for output.
/// </para>
/// </remarks>
public sealed record RunTotals(
    int RunCount,
    long DurationMs,
    int NumTurns,
    decimal TotalCostUsd,
    TokenTotal InputTokens,
    TokenTotal OutputTokens);
