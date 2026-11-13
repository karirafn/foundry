import type { IssueState } from '../../shared/utils/issue-state';
export type { IssueState } from '../../shared/utils/issue-state';
import { WORKING_STATES } from '../../shared/utils/issue-state';
import type { RevisionTriggerKind } from '../../shared/utils/revision-trigger-kind';
import type { TokenTotal } from '../workers/run-totals.model';

export interface RunStats {
  runCount: number;
  durationMs: number | null;
  numTurns: number | null;
  totalCostUsd: number | null;
  /** Billable input, summed over known runs; see TokenTotal for the partial-sum rule. */
  inputTokens: TokenTotal;
  outputTokens: TokenTotal;
}

/** Publish phase of a working issue's run; null while the agent runs or once publishing finished. */
export type PublishPhase = 'publishing' | 'retrying';

/**
 * The part of the lifecycle that owns the issue, as the server declares it. `done` is a known value
 * that the board never places.
 */
export type IssueStage = 'triage' | 'implement' | 'awaiting_review' | 'done';

/** The step Foundry's own code runs for the issue, with no worker involved. */
export type CodeStep = 'publishing' | 'publish_retrying' | 'rebasing' | 'checks';

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
  checksOutcome?: string | null;
  rebaseOutcome?: string | null;
  rebaseSkippedReason?: string | null;
  pullRequestUrl?: string | null;
  providerType?: string | null;
  revisionTriggerKind?: RevisionTriggerKind | null;
  /**
   * The publish phase of the issue's run, non-null only while the issue is in_progress or
   * revision_in_progress: Foundry is publishing the run's commits, or retrying a failed publish.
   */
  publishPhase?: PublishPhase | null;
  /**
   * Placement on the board, decided by the server (#851). Null when the server sent a stage this
   * dashboard does not know; the board leaves such a card out.
   */
  stage: IssueStage | null;
  /** True when the issue progresses only after a person acts. */
  needsAction: boolean;
  /** True while the issue waits for a worker run to be dispatched. */
  awaitsDispatch: boolean;
  codeStep: CodeStep | null;
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

export interface FailingCheck {
  name: string;
  conclusion: string;
  detailsUrl?: string | null;
}

export type { RevisionTriggerKind } from '../../shared/utils/revision-trigger-kind';

export interface InputQuestion {
  question: string;
  context: string;
  recommendation?: string | null;
}

export type QuestionPostStatus = 'pending' | 'posted' | 'failed';

/** Where an awaiting-input issue goes once a reply settles; `retriage` means triage asked. */
export type ResumeTarget = 'revision' | 'continuation' | 'fresh' | 'retriage';

export interface AwaitingInputDetails {
  questions: InputQuestion[];
  postStatus: QuestionPostStatus;
  postedAt: string | null;
  commentUrl: string | null;
  postFailureReason: string | null;
  nextPostAttemptAt: string | null;
  resumeTarget: ResumeTarget;
}

export type IssueKind = 'feature' | 'bug' | 'refactor' | 'documentation';

export interface IssueKindClassification {
  source: 'labelled' | 'triaged' | 'unclassified';
  /** Null only when source is 'unclassified'. */
  kind: IssueKind | null;
  /** Set only when source is 'labelled' and triage judged a different kind. */
  conflictingTriageKind: IssueKind | null;
}

export interface TriageVerdictDetails {
  outcome: 'ready' | 'needs_input' | 'blocked';
  kind: IssueKind;
  questions: string[];
  /** ISO-8601. */
  decidedAt: string;
}

export interface BlockerDetails {
  issueNumber: number;
  reason: string | null;
}

export interface IssueStateDetails {
  workerRunId: string | null;
  branchName: string | null;
  feedbackCutoffAt: string | null;
  failureReason: string | null;
  failedAt: string | null;
  completedAt: string | null;
  blockedBy: BlockerDetails[] | null;
  violations: EligibilityViolation[] | null;
  transientRetry: TransientRetryDetails | null;
  checksOutcome?: string | null;
  failingChecks?: FailingCheck[] | null;
  revisionTriggerKind?: RevisionTriggerKind | null;
  checksFixAttempts?: number | null;
  rebaseConflictAttempts?: number | null;
  rebaseOutcome?: string | null;
  rebaseSkippedReason?: string | null;
  awaitingInput?: AwaitingInputDetails | null;
  /** Set when the state is branch_diverged: the issue branch on the provider (URL built by the API). */
  branchUrl?: string | null;
  /** Set when the state is branch_diverged: the side branch Foundry pushed the run's work to. */
  sideBranchName?: string | null;
  sideBranchUrl?: string | null;
  /** Set when the state is branch_diverged: why the run's work went to the side branch (ADR 0101). */
  divergenceReason?: DivergenceReason | null;
}

/** Why the Publisher pushed a run's work to a side branch: the branch moved during the run, or it is protected from force pushes. */
export type DivergenceReason = 'branch-moved' | 'force-push-protected';

export interface IssueDetail extends IssueSummary {
  author: string;
  labels: string[];
  stateDetails: IssueStateDetails | null;
  kind: IssueKindClassification;
  /** The latest triage verdict; null if the issue was never triaged. */
  triage: TriageVerdictDetails | null;
}

// Live states: a worker is running: implementing, revising or triaging.
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const LIVE_STATES: ReadonlySet<IssueState> = WORKING_STATES;

// The four queued tiers dispatched in Dispatch Order: revision > continuation > triage > fresh.
export const QUEUED_TIER_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'revision_queued',
  'continuation_queued',
  'triage_queued',
  'queued',
]);
