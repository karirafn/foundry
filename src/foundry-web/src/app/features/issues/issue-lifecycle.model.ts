import { IssueState } from './issue.model';

export interface StateGroup {
  readonly label: string;
  readonly states: readonly IssueState[];
}

export const ACTIVE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'detected',
  'queued',
  'blocked',
  'in_progress',
  'review',
  'failed',
  'continuable_failed',
  'continuation_queued',
  'revision_queued',
  'revision_in_progress',
  'revision_failed',
]);

export const RESOLVED_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'completed',
  'unchanged',
]);

export function isResolvedState(state: IssueState): boolean {
  return RESOLVED_STATES.has(state);
}

export const STATE_GROUPS: readonly StateGroup[] = [
  {
    label: 'In progress',
    states: ['in_progress', 'revision_in_progress', 'continuation_queued'],
  },
  {
    label: 'Waiting',
    states: ['detected', 'queued', 'blocked', 'revision_queued'],
  },
  {
    label: 'Needs attention',
    states: ['review', 'failed', 'continuable_failed', 'revision_failed'],
  },
  {
    label: 'Resolved',
    states: ['completed', 'unchanged'],
  },
] as const;
