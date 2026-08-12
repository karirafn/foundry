import type { IssueState } from '../../shared/utils/issue-state';
export type { IssueState } from '../../shared/utils/issue-state';
import { WORKING_STATES } from '../../shared/utils/issue-state';

export interface RunStats {
  runCount: number;
  durationMs: number | null;
  numTurns: number | null;
  totalCostUsd: number | null;
  inputTokens: number | null;
  outputTokens: number | null;
}

export interface IssueSummary {
  id: string;
  issueNumber: number;
  title: string;
  state: IssueState;
  repositorySlug: string;
  detectedAt: string;
  url: string;
  failureClassification?: string;
  repositoryEligibilityStatus?: string | null;
  runStats?: RunStats | null;
}

export interface EligibilityViolation {
  rule: string;
  description: string;
}

export interface TransientRetryDetails {
  attemptNumber: number;
  maxAttempts: number;
  isExhausted: boolean;
  nextAttemptDueAt: string | null;
}

export interface IssueStateDetails {
  workerRunId: string | null;
  branchName: string | null;
  pullRequestUrl: string | null;
  feedbackCutoffAt: string | null;
  failureReason: string | null;
  failedAt: string | null;
  completedAt: string | null;
  blockedBy: number[] | null;
  violations: EligibilityViolation[] | null;
  transientRetry: TransientRetryDetails | null;
}

export interface IssueDetail extends IssueSummary {
  providerType: string;
  author: string;
  labels: string[];
  stateDetails: IssueStateDetails | null;
}

// Live states: issues with an active worker running (in progress or under revision).
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const LIVE_STATES: ReadonlySet<IssueState> = WORKING_STATES;

// The three queued tiers dispatched in Dispatch Order: revision > continuation > fresh.
export const QUEUED_TIER_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'revision_queued',
  'continuation_queued',
  'queued',
]);
