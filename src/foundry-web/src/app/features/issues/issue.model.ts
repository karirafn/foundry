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
export type CodeStep = 'publishing' | 'publish_retrying' | 'rebasing' | 'checks' | 'merging';

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
  /** Set on a review issue Foundry did not merge itself: `disabled` or `skipped` (ADR 0105). */
  mergeOutcome?: string | null;
  /** Set when mergeOutcome is `skipped`: why Foundry did not merge. */
  mergeSkippedReason?: string | null;
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
  /** True while the issue waits for its open blockers to close (#892). */
  waitsOnBlockers: boolean;
  codeStep: CodeStep | null;
  /** The monitored repository the issue belongs to: the id the repository list and settings route use. */
  repositoryId: string;
  /** When the issue entered its current state (ISO 8601). */
  enteredStateAt: string;
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
  /** Set on a review issue Foundry did not merge itself: `disabled` or `skipped` (ADR 0105). */
  mergeOutcome?: string | null;
  mergeSkippedReason?: string | null;
  awaitingInput?: AwaitingInputDetails | null;
  /** Set when the state is branch_diverged: the issue branch on the provider (URL built by the API). */
  branchUrl?: string | null;
  /** Set when the state is branch_diverged: the side branch Foundry pushed the run's work to. */
  sideBranchName?: string | null;
  sideBranchUrl?: string | null;
  /** Set when the state is branch_diverged: why the run's work went to the side branch (ADR 0101). */
  divergenceReason?: DivergenceReason | null;
  /** Set when the state is checks_pending: why Foundry is waiting for CI on the PR/MR. */
  checksPendingReason?: ChecksPendingReason | null;
  /** Set when the state is rebase_pending: why Foundry is rebasing the PR/MR onto the default branch. */
  rebasePendingReason?: RebasePendingReason | null;
  /** Set while a catch-up has started (ADR 0111): the catch-up round the PR/MR is on. */
  catchUpRound?: number | null;
  /** Set with catchUpRound: how many catch-up rounds a PR/MR may start. */
  catchUpRoundLimit?: number | null;
  /** Set when the state is merge_pending and the PR/MR waits in the catch-up queue: its place and what it waits for. */
  mergeWait?: MergeWaitDetails | null;
}

/** Why the Publisher pushed a run's work to a side branch: the branch moved during the run, or it is protected from force pushes. */
export type DivergenceReason = 'branch-moved' | 'force-push-protected';

/** Why an issue is in checks_pending: a run left an open PR/MR, new commits reached it after its checks resolved, its checks passed on a rerun of the same commits, or it was rebased for a catch-up. */
export type ChecksPendingReason = 'after_run' | 'head_moved' | 'checks_rerun' | 'catch_up';

/** Why an issue is in rebase_pending: a run left an open PR/MR, or its merge was refused because it fell behind the default branch (ADR 0111). */
export type RebasePendingReason = 'after_run' | 'catch_up';

/** A merge-pending PR/MR's place in the catch-up queue: position 1 is next to catch up; waitingForIssueNumber is the issue whose up-to-date PR/MR merges first. */
export interface MergeWaitDetails {
  position: number;
  waitingForIssueNumber: number | null;
}

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
