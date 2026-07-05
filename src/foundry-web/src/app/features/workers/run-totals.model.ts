export interface RunTotals {
  readonly runCount: number;
  readonly durationMs: number;
  readonly numTurns: number;
  readonly totalCostUsd: number;
  readonly inputTokens: number;
  readonly outputTokens: number;
}

export type StatsScope = 'month' | 'all';
