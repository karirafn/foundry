import { IssueState, WORKING_STATES } from './issue-state';

export const STATE_LABELS: Record<IssueState, string> = {
  detected: 'DETECTED',
  queued: 'QUEUED',
  blocked: 'BLOCKED',
  in_progress: 'IN PROGRESS',
  review: 'REVIEW',
  rebase_pending: 'REBASING',
  checks_pending: 'CHECKS',
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
  rebase_pending: 'rebasing',
  checks_pending: 'checks pending',
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
  rebase_pending: 'badge--rebase-pending',
  checks_pending: 'badge--checks-pending',
  unchanged: 'badge--unchanged',
  failed: 'badge--failed',
  continuable_failed: 'badge--continuable-failed',
  continuation_queued: 'badge--continuation-queued',
  completed: 'badge--completed',
  revision_queued: 'badge--revision-queued',
  revision_in_progress: 'badge--revision-in-progress',
  revision_failed: 'badge--revision-failed',
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
