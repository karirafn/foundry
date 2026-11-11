import { IssueState, WORKING_STATES } from './issue-state';
import { getFailureCategoryDisplay } from './failure-category';
import type { RevisionTriggerKind } from './revision-trigger-kind';

export const STATE_LABELS: Record<IssueState, string> = {
  detected: 'DETECTED',
  queued: 'QUEUED',
  blocked: 'BLOCKED',
  in_progress: 'IN PROGRESS',
  review: 'REVIEW',
  rebase_pending: 'REBASING',
  checks_pending: 'CHECKS',
  awaiting_input: 'AWAITING INPUT',
  unchanged: 'UNCHANGED',
  failed: 'FAILED',
  continuable_failed: 'CONT FAILED',
  continuation_queued: 'CONT QUEUED',
  completed: 'COMPLETED',
  revision_queued: 'REV QUEUED',
  revision_in_progress: 'REV IN PROGRESS',
  revision_failed: 'REV FAILED',
  triage_queued: 'TRIAGE QUEUED',
  triage_in_progress: 'TRIAGING',
  triage_failed: 'TRIAGE FAILED',
};

export const STATE_ARIA_LABELS: Record<IssueState, string> = {
  detected: 'detected',
  queued: 'queued',
  blocked: 'blocked',
  in_progress: 'in progress',
  review: 'review',
  rebase_pending: 'rebasing',
  checks_pending: 'checks pending',
  awaiting_input: 'awaiting input',
  unchanged: 'unchanged',
  failed: 'failed',
  continuable_failed: 'continuable failed',
  continuation_queued: 'continuation queued',
  completed: 'completed',
  revision_queued: 'revision queued',
  revision_in_progress: 'revision in progress',
  revision_failed: 'revision failed',
  triage_queued: 'triage queued',
  triage_in_progress: 'triaging',
  triage_failed: 'triage failed',
};

export const STATE_CSS_CLASSES: Record<IssueState, string> = {
  detected: 'badge--detected',
  queued: 'badge--queued',
  blocked: 'badge--blocked',
  in_progress: 'badge--in-progress',
  review: 'badge--review',
  rebase_pending: 'badge--rebase-pending',
  checks_pending: 'badge--checks-pending',
  awaiting_input: 'badge--awaiting-input',
  unchanged: 'badge--unchanged',
  failed: 'badge--failed',
  continuable_failed: 'badge--continuable-failed',
  continuation_queued: 'badge--continuation-queued',
  completed: 'badge--completed',
  revision_queued: 'badge--revision-queued',
  revision_in_progress: 'badge--revision-in-progress',
  revision_failed: 'badge--revision-failed',
  triage_queued: 'badge--triage-queued',
  triage_in_progress: 'badge--triage-in-progress',
  triage_failed: 'badge--triage-failed',
};

export function cardAccentFor(state: IssueState): 'working' | 'ready' | null {
  if (WORKING_STATES.has(state)) {
    return 'working';
  }
  if (state === 'review') {
    return 'ready';
  }
  return null;
}

export interface StateDisplay {
  readonly label: string;
  /** Bare lowercase phrase; callers add any `State: ` prefix. */
  readonly ariaLabel: string;
  readonly cssClass: string;
}

export interface StateDisplayOptions {
  readonly failureClassification?: string | null;
  readonly revisionTriggerKind?: RevisionTriggerKind | null;
}

type RevisionState = Extract<IssueState, 'revision_queued' | 'revision_in_progress' | 'revision_failed'>;

interface TriggerLabel {
  readonly label: string;
  readonly ariaLabel: string;
}

// Typed over the full union so a new trigger kind cannot ship without labels.
const REVISION_TRIGGER_LABELS: Record<RevisionTriggerKind, Record<RevisionState, TriggerLabel>> = {
  rebase_conflict: {
    revision_queued: { label: 'CONFLICT QUEUED', ariaLabel: 'conflict queued' },
    revision_in_progress: { label: 'RESOLVING CONFLICTS', ariaLabel: 'resolving conflicts' },
    revision_failed: { label: 'CONFLICT FAILED', ariaLabel: 'conflict failed' },
  },
  failed_checks: {
    revision_queued: { label: 'CHECKS FIX QUEUED', ariaLabel: 'checks fix queued' },
    revision_in_progress: { label: 'FIXING CHECKS', ariaLabel: 'fixing checks' },
    revision_failed: { label: 'CHECKS FIX FAILED', ariaLabel: 'checks fix failed' },
  },
  input_provided: {
    revision_queued: { label: 'RESUME QUEUED', ariaLabel: 'resume queued' },
    revision_in_progress: { label: 'RESUMING', ariaLabel: 'resuming' },
    revision_failed: { label: 'RESUME FAILED', ariaLabel: 'resume failed' },
  },
  review_feedback: {
    revision_queued: { label: STATE_LABELS.revision_queued, ariaLabel: STATE_ARIA_LABELS.revision_queued },
    revision_in_progress: { label: STATE_LABELS.revision_in_progress, ariaLabel: STATE_ARIA_LABELS.revision_in_progress },
    revision_failed: { label: STATE_LABELS.revision_failed, ariaLabel: STATE_ARIA_LABELS.revision_failed },
  },
};

const FAILURE_CATEGORY_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['failed', 'continuable_failed']);

function isRevisionState(state: IssueState): state is RevisionState {
  return state === 'revision_queued' || state === 'revision_in_progress' || state === 'revision_failed';
}

// The kind arrives as a server string, so an unknown future trigger must not index the table.
function triggerLabelFor(state: IssueState, kind: RevisionTriggerKind | null | undefined): TriggerLabel | null {
  if (kind == null || !isRevisionState(state) || !Object.prototype.hasOwnProperty.call(REVISION_TRIGGER_LABELS, kind)) {
    return null;
  }
  return REVISION_TRIGGER_LABELS[kind][state];
}

/**
 * The single source of an issue's state label, ARIA phrase and badge class.
 * Precedence: failure category, then revision trigger, then the plain state.
 */
export function resolveStateDisplay(state: IssueState, options: StateDisplayOptions = {}): StateDisplay {
  const classification = options.failureClassification;
  if (classification != null && FAILURE_CATEGORY_STATES.has(state)) {
    const failure = getFailureCategoryDisplay(classification);
    if (failure !== null) {
      return { label: failure.label, ariaLabel: failure.label.toLowerCase(), cssClass: failure.cssClass };
    }
  }

  const trigger = triggerLabelFor(state, options.revisionTriggerKind);
  if (trigger !== null) {
    return { label: trigger.label, ariaLabel: trigger.ariaLabel, cssClass: STATE_CSS_CLASSES[state] };
  }

  return { label: STATE_LABELS[state], ariaLabel: STATE_ARIA_LABELS[state], cssClass: STATE_CSS_CLASSES[state] };
}
