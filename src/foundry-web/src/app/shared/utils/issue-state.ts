export type IssueState =
  | 'detected'
  | 'queued'
  | 'blocked'
  | 'in_progress'
  | 'review'
  | 'rebase_pending'
  | 'checks_pending'
  | 'awaiting_input'
  | 'unchanged'
  | 'failed'
  | 'continuable_failed'
  | 'continuation_queued'
  | 'completed'
  | 'revision_queued'
  | 'revision_in_progress'
  | 'revision_failed'
  | 'triage_queued'
  | 'triage_in_progress'
  | 'triage_failed'
  | 'publish_failed'
  | 'branch_diverged';

// Display-only cap for the "attempt n of MAX" label on the fixing-checks revision callout.
// Mirrors the server-side ChecksPendingIssue.ChecksMaxFixPushes value.
export const MAX_CHECKS_FIX_ATTEMPTS = 2;

// Display-only cap for the "attempt n of MAX" label on the resolving-conflicts revision callout.
// Mirrors the server-side RebasePendingIssue.MaxRebaseConflictAttempts value.
export const MAX_REBASE_CONFLICT_ATTEMPTS = 2;

// Live states: a worker is running: implementing, revising or triaging.
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const WORKING_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'in_progress',
  'revision_in_progress',
  'triage_in_progress',
]);

// Retryable states: the backend accepts a manual retry request for these.
// unchanged is included because a run that produced no changes can be retried.
// review is intentionally excluded: it has its own feedback flow.
// triage_failed is intentionally excluded: its retry is a separate endpoint (Retry triage), and
// Retry Issue would skip triage.
// branch_diverged is included: Retry Issue continues from the issue branch.
// publish_failed is intentionally excluded: Retry Issue would abandon the unpublished run, so the
// issue offers Retry publish instead.
export const RETRYABLE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'failed',
  'continuable_failed',
  'revision_failed',
  'unchanged',
  'branch_diverged',
]);

// Revision states: a revision worker is queued or actively running.
// revision_failed is intentionally excluded: that state has its own failure callout
// (the issue transitions to retryable) and must not show the in-progress revision chip.
export const REVISION_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'revision_queued',
  'revision_in_progress',
]);

// Re-triageable states: the backend accepts a Re-triage request for these, sending the issue
// back through triage.
export const RETRIAGEABLE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'queued',
  'blocked',
]);
