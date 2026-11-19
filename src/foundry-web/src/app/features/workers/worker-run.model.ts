import { components } from '../../api/schema';
import { ToolchainEntry } from '../../core/models/toolchain.model';

export type WorkerRunState = 'starting' | 'running' | 'publishing' | 'publish_failed' | 'publish_blocked' | 'completed' | 'failed';

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

/**
 * A possible secret the diff gate found in a run's changes. The matched value is never sent
 * to the browser: there is no field for it.
 */
export interface SecretFinding {
  readonly path: string;
  readonly line: number;
  readonly commitSha: string;
  readonly ruleId: string;
}

/** A finding an operator confirmed as a false positive. Like a secret finding, it has no matched value. */
export interface ConfirmedFinding {
  readonly path: string;
  readonly line: number;
  readonly ruleId: string;
}

export type RevertedFileReason = 'oversized' | 'binary';

/** A file the diff gate reverted in a separate commit before pushing. */
export interface RevertedFile {
  readonly path: string;
  readonly reason: RevertedFileReason;
  readonly sizeBytes: number;
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
  /** The diff gate's findings that blocked the push; empty when none. */
  readonly secretFindings: readonly SecretFinding[];
  /** Files the diff gate reverted before pushing; empty when none. */
  readonly revertedFiles: readonly RevertedFile[];
  /** When the diff gate blocked the push; set only while the run state is publish_blocked. A confirmation echoes it. */
  readonly publishBlockedAt: string | null;
  /** When an operator last confirmed the run's findings as false positives; null when never. */
  readonly falsePositivesConfirmedAt: string | null;
  /** Every finding an operator confirmed as a false positive on this run; empty when none. */
  readonly confirmedFindings: readonly ConfirmedFinding[];
  /** The toolchain the run's container reported when it started, in its order; null when it reported none. */
  readonly toolchain: readonly ToolchainEntry[] | null;
  /** The valid verdict a review run recorded, with the outcome Foundry derived from it; absent or null for any other run. */
  readonly reviewVerdict?: RecordedReviewVerdict | null;
  /** Foundry's own test check on a review run: production code changed while no test did; null without review facts. */
  readonly productionChangedWithoutTests?: boolean | null;
  /** The acceptance criteria Foundry supplied to a review run, which its verdict answers by id; null without review facts. */
  readonly reviewCriteria?: readonly ReviewCriterion[] | null;
}

/** A review verdict as the run recorded it (ADR 0113). */
export type RecordedReviewVerdict = components['schemas']['RecordedReviewVerdict'];
export type ReviewFinding = components['schemas']['ReviewFinding'];
export type ReviewCategory = components['schemas']['ReviewCategory'];
export type ReviewVerdictOutcome = components['schemas']['ReviewVerdictOutcome'];
export type ReviewCriterion = components['schemas']['WorkerRunReviewCriterion'];

/** Run states in which the agent has stopped and Foundry owns the run's commits. */
export const PUBLISH_RUN_STATES: ReadonlySet<WorkerRunState> = new Set<WorkerRunState>([
  'publishing',
  'publish_failed',
  'publish_blocked',
]);
