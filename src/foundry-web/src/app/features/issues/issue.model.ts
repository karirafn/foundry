export type IssueState =
  | 'detected'
  | 'queued'
  | 'blocked'
  | 'in_progress'
  | 'review'
  | 'unchanged'
  | 'failed'
  | 'continuable_failed'
  | 'continuation_queued'
  | 'completed'
  | 'revision_queued'
  | 'revision_in_progress'
  | 'revision_failed'
  | 'ineligible';

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
}

export interface IssueDetail extends IssueSummary {
  providerType: string;
  author: string;
  labels: string[];
  stateDetails: IssueStateDetails | null;
}

// Live states: issues with an active worker running (in progress or under revision).
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const LIVE_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['in_progress', 'revision_in_progress']);

// The three queued tiers dispatched in Dispatch Order: revision > continuation > fresh.
export const QUEUED_TIER_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'revision_queued',
  'continuation_queued',
  'queued',
]);
