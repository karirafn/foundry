export type IssueState =
  | 'detected'
  | 'queued'
  | 'blocked'
  | 'in_progress'
  | 'review'
  | 'checks_pending'
  | 'unchanged'
  | 'failed'
  | 'continuable_failed'
  | 'continuation_queued'
  | 'completed'
  | 'revision_queued'
  | 'revision_in_progress'
  | 'revision_failed';

// Display-only cap for the "attempt n of MAX" label on the fixing-checks revision callout.
// Mirrors the server-side ChecksPendingIssue.ChecksMaxFixPushes value.
export const MAX_CHECKS_FIX_ATTEMPTS = 2;

// Live states: issues with an active worker running (in progress or under revision).
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const WORKING_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['in_progress', 'revision_in_progress']);

// Retryable states: the backend accepts a manual retry request for these.
// unchanged is included because a run that produced no changes can be retried.
// review is intentionally excluded: it has its own feedback flow.
export const RETRYABLE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'failed',
  'continuable_failed',
  'revision_failed',
  'unchanged',
]);

// Revision states: a revision worker is queued or actively running.
// revision_failed is intentionally excluded: that state has its own failure callout
// (the issue transitions to retryable) and must not show the in-progress revision chip.
export const REVISION_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'revision_queued',
  'revision_in_progress',
]);
