export type WorkerRunState = 'starting' | 'running' | 'publishing' | 'publish_failed' | 'completed' | 'failed';

/** What Foundry published for a run. Provider URLs are built by the API. */
export interface PublishFacts {
  readonly headSha: string;
  readonly headCommitUrl: string | null;
  /** Null when no pull request was opened (a failed run publishes to the branch only). */
  readonly pullRequestUrl: string | null;
  readonly branchCommitCount: number;
}

/** The automatic publish retry of a run waiting to publish. */
export interface PublishRetry {
  readonly attemptNumber: number;
  readonly maxAttempts: number;
  readonly nextAttemptAt: string | null;
  /** Operator text produced and redacted by the API; it never contains the token. */
  readonly reason: string;
}

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
  /** Set on any run that published, completed or failed. */
  readonly publish: PublishFacts | null;
  /** Set while the run state is publish_failed. */
  readonly publishRetry: PublishRetry | null;
  /** When the TTL backstop discards the unpublished run; set while the run holds unpublished work. */
  readonly discardsAt: string | null;
}

/** Run states in which the agent has stopped and Foundry owns the run's commits. */
export const PUBLISH_RUN_STATES: ReadonlySet<WorkerRunState> = new Set<WorkerRunState>(['publishing', 'publish_failed']);
