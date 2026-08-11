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
  | 'revision_failed';

// Live states: issues with an active worker running (in progress or under revision).
// continuation_queued is intentionally excluded: it is a queued tier, not an active worker state.
export const WORKING_STATES: ReadonlySet<IssueState> = new Set<IssueState>(['in_progress', 'revision_in_progress']);
