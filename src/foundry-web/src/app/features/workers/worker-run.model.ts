export type WorkerRunState = 'starting' | 'running' | 'completed' | 'failed';

export interface WorkerRunDetail {
  readonly workerRunId: string;
  readonly issueId: string;
  readonly role: string;
  readonly state: WorkerRunState;
  readonly failureCategory: string | null;
  readonly failureSummary: string | null;
  readonly resultText: string | null;
  readonly subtype: string | null;
  readonly isError: boolean | null;
  readonly durationMs: number | null;
  readonly numTurns: number | null;
  readonly totalCostUsd: number | null;
  readonly inputTokens: number | null;
  readonly outputTokens: number | null;
  readonly lastActivityAt: string | null;
  readonly hasStoredLog: boolean;
  /** Set once a user asked to cancel the run; carried into the completed/failed run. */
  readonly cancelRequestedAt: string | null;
}
