export interface RunTotals {
  readonly runCount: number;
  readonly durationMs: number;
  readonly numTurns: number;
  readonly totalCostUsd: number;
  /** Billable input; null when any contributing run's figure is unknown. */
  readonly inputTokens: number | null;
  /** Null when any contributing run's figure is unknown. */
  readonly outputTokens: number | null;
}

export type StatsScope = 'month' | 'all';
