namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Aggregated telemetry totals across all worker runs in a time window.
/// All fields are non-nullable: the value is zero when there are no runs or when no completed
/// runs contribute to a given dimension.
/// </summary>
/// <remarks>
/// In-progress runs (<c>StartingRun</c> and <c>ActiveRun</c>) contribute to
/// <see cref="RunCount"/> but contribute nothing to <see cref="DurationMs"/>,
/// <see cref="NumTurns"/>, <see cref="TotalCostUsd"/>, <see cref="InputTokens"/>,
/// or <see cref="OutputTokens"/>, because telemetry is only available once a run completes.
/// </remarks>
public sealed record RunTotals(
    int RunCount,
    long DurationMs,
    int NumTurns,
    decimal TotalCostUsd,
    long InputTokens,
    long OutputTokens);
