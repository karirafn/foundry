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
  'unchanged',
  'failed',
  'continuable_failed',
  'continuation_queued',
  'revision_queued',
  'revision_in_progress',
  'revision_failed',
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
  'ineligible',
]);

export function isKnownState(state: string): state is IssueState {
  return (KNOWN_STATES as ReadonlySet<string>).has(state);
}

export const STATE_GROUPS: readonly StateGroup[] = [
  {
    label: 'In progress',
    states: ['in_progress', 'revision_in_progress'],
  },
  {
    label: 'Needs attention',
    states: ['review', 'unchanged', 'failed', 'continuable_failed', 'revision_failed'],
  },
  {
    label: 'Waiting',
    states: ['detected', 'queued', 'blocked', 'revision_queued', 'continuation_queued'],
  },
  {
    label: 'Resolved',
    states: ['completed'],
  },
] as const;

export const UNGROUPED_RANK = STATE_GROUPS.length;

const groupRankMap = new Map<IssueState, number>(
  STATE_GROUPS.flatMap((group, index) =>
    group.states.map((state): [IssueState, number] => [state, index])
  )
);

export function groupRankFor(state: IssueState): number {
  return groupRankMap.get(state) ?? UNGROUPED_RANK;
}

// Declarative within-group tier ordering. Each nested array is a peer tier;
// states in the same tier have equal rank. Tier index = rank.
export const WITHIN_GROUP_ORDER: Readonly<Record<string, readonly (readonly IssueState[])[]>> = {
  'In progress': [
    ['in_progress', 'revision_in_progress'],
  ],
  'Needs attention': [
    ['review', 'unchanged', 'failed', 'continuable_failed', 'revision_failed'],
  ],
  'Waiting': [
    ['queued', 'revision_queued', 'continuation_queued'],
    ['detected'],
    ['blocked'],
  ],
  'Resolved': [
    ['completed'],
  ],
} as const;

const withinGroupRankMap = new Map<IssueState, number>(
  Object.values(WITHIN_GROUP_ORDER).flatMap((tiers) =>
    tiers.flatMap((tier, tierIndex) =>
      tier.map((state): [IssueState, number] => [state, tierIndex])
    )
  )
);

export function withinGroupRankFor(state: IssueState): number {
  return withinGroupRankMap.get(state) ?? 0;
}
