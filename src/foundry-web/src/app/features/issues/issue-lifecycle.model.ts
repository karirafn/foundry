import { IssueState } from './issue.model';

export const ACTIVE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'detected',
  'queued',
  'blocked',
  'triage_blocked',
  'in_progress',
  'review',
  'rebase_pending',
  'checks_pending',
  'awaiting_input',
  'unchanged',
  'failed',
  'continuable_failed',
  'continuation_queued',
  'revision_queued',
  'revision_in_progress',
  'revision_failed',
  'triage_queued',
  'triage_in_progress',
  'triage_failed',
  'publish_failed',
  'publish_blocked',
  'branch_diverged',
]);

export const RESOLVED_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'completed',
]);

export function isResolvedState(state: IssueState): boolean {
  return RESOLVED_STATES.has(state);
}

const KNOWN_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  ...ACTIVE_STATES,
  ...RESOLVED_STATES,
]);

export function isKnownState(state: string): state is IssueState {
  return (KNOWN_STATES as ReadonlySet<string>).has(state);
}
