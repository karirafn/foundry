/**
 * A token metric summed across runs. `total` sums only the runs whose figure is known, so it is
 * exact when `unknownRunCount` is 0 and a lower bound otherwise. When `knownRunCount` is 0 and
 * `unknownRunCount` is positive, no figure is known and the total must render as unknown.
 */
export interface TokenTotal {
  readonly total: number;
  readonly knownRunCount: number;
  readonly unknownRunCount: number;
}

export interface RunTotals {
  readonly runCount: number;
  readonly durationMs: number;
  readonly numTurns: number;
  readonly totalCostUsd: number;
  /** Billable input (uncached + cache-read + cache-write), summed over known runs. */
  readonly inputTokens: TokenTotal;
  readonly outputTokens: TokenTotal;
}

export type StatsScope = 'month' | 'all';
