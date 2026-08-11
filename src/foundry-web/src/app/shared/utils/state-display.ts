import { IssueState, WORKING_STATES } from './issue-state';

export const STATE_LABELS: Record<IssueState, string> = {
  detected: 'DETECTED',
  queued: 'QUEUED',
  blocked: 'BLOCKED',
  in_progress: 'IN PROGRESS',
  review: 'REVIEW',
  unchanged: 'UNCHANGED',
  failed: 'FAILED',
  continuable_failed: 'CONT FAILED',
  continuation_queued: 'CONT QUEUED',
  completed: 'COMPLETED',
  revision_queued: 'REV QUEUED',
  revision_in_progress: 'REV IN PROGRESS',
  revision_failed: 'REV FAILED',
};

export const STATE_ARIA_LABELS: Record<IssueState, string> = {
  detected: 'detected',
  queued: 'queued',
  blocked: 'blocked',
  in_progress: 'in progress',
  review: 'review',
  unchanged: 'unchanged',
  failed: 'failed',
  continuable_failed: 'continuable failed',
  continuation_queued: 'continuation queued',
  completed: 'completed',
  revision_queued: 'revision queued',
  revision_in_progress: 'revision in progress',
  revision_failed: 'revision failed',
};

export const STATE_CSS_CLASSES: Record<IssueState, string> = {
  detected: 'badge--detected',
  queued: 'badge--queued',
  blocked: 'badge--blocked',
  in_progress: 'badge--in-progress',
  review: 'badge--review',
  unchanged: 'badge--unchanged',
  failed: 'badge--failed',
  continuable_failed: 'badge--continuable-failed',
  continuation_queued: 'badge--continuation-queued',
  completed: 'badge--completed',
  revision_queued: 'badge--revision-queued',
  revision_in_progress: 'badge--revision-in-progress',
  revision_failed: 'badge--revision-failed',
};

/**
 * Human-readable rail labels — title-cased, suitable for UI display in the filter rail.
 * Distinct from STATE_LABELS which uses short all-caps badge text.
 */
export const STATE_RAIL_LABELS: Record<IssueState, string> = {
  detected: 'Detected',
  queued: 'Queued',
  blocked: 'Blocked',
  in_progress: 'In Progress',
  review: 'Review',
  unchanged: 'Unchanged',
  failed: 'Failed',
  continuable_failed: 'Continuable Failed',
  continuation_queued: 'Continuation Queued',
  completed: 'Completed',
  revision_queued: 'Revision Queued',
  revision_in_progress: 'Revision In Progress',
  revision_failed: 'Revision Failed',
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

/**
 * CSS custom property name for per-state color, used by the filter rail dot indicator.
 * Maps each state to the --fd-state-* token from styles.scss.
 */
export const STATE_COLOR_VARS: Record<IssueState, string> = {
  detected: 'var(--fd-state-detected)',
  queued: 'var(--fd-state-queued)',
  blocked: 'var(--fd-state-blocked)',
  in_progress: 'var(--fd-state-in-progress)',
  review: 'var(--fd-state-review)',
  unchanged: 'var(--fd-state-unchanged)',
  failed: 'var(--fd-state-failed)',
  continuable_failed: 'var(--fd-state-failed)',
  continuation_queued: 'var(--fd-state-queued)',
  completed: 'var(--fd-state-completed)',
  revision_queued: 'var(--fd-state-queued)',
  revision_in_progress: 'var(--fd-state-in-progress)',
  revision_failed: 'var(--fd-state-failed)',
};
