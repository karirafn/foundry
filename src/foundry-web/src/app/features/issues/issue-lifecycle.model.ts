import { IssueState } from './issue.model';

export type StateGroupId = 'in_progress' | 'needs_attention' | 'waiting' | 'resolved';

export interface StateGroup {
  readonly id: StateGroupId;
  readonly label: string;
  readonly states: readonly IssueState[];
}

export const ACTIVE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'detected',
  'queued',
  'blocked',
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

export const STATE_GROUPS: readonly StateGroup[] = [
  {
    id: 'in_progress',
    label: 'In progress',
    states: ['in_progress', 'revision_in_progress', 'triage_in_progress'],
  },
  {
    id: 'needs_attention',
    label: 'Needs attention',
    states: [
      'review',
      'awaiting_input',
      'unchanged',
      'failed',
      'continuable_failed',
      'revision_failed',
      'triage_failed',
      'publish_failed',
      'publish_blocked',
      'branch_diverged',
    ],
  },
  {
    id: 'waiting',
    label: 'Waiting',
    states: [
      'detected',
      'queued',
      'blocked',
      'revision_queued',
      'continuation_queued',
      'triage_queued',
      'rebase_pending',
      'checks_pending',
    ],
  },
  {
    id: 'resolved',
    label: 'Resolved',
    states: ['completed'],
  },
] as const;

const groupRankMap = new Map<IssueState, number>(
  STATE_GROUPS.flatMap((group, index) =>
    group.states.map((state): [IssueState, number] => [state, index])
  )
);

const groupByStateMap = new Map<IssueState, StateGroup>(
  STATE_GROUPS.flatMap((group) =>
    group.states.map((state): [IssueState, StateGroup] => [state, group])
  )
);

export const RESOLVED_GROUP_ID: StateGroupId = 'resolved';

// Groups selected on a fresh page load: every active group, Resolved off (its band is paginated
// and fetched on demand).
export const DEFAULT_SELECTED_GROUPS: ReadonlySet<StateGroupId> = new Set<StateGroupId>(
  STATE_GROUPS.map((group) => group.id).filter((id) => id !== RESOLVED_GROUP_ID)
);

export function groupFor(state: IssueState): StateGroup {
  // Non-null assertion is safe: every IssueState is assigned to exactly one group in STATE_GROUPS.
  // The partition-guard test in issue-lifecycle.model.spec.ts enforces totality.
  return groupByStateMap.get(state)!;
}

export function groupById(id: StateGroupId): StateGroup {
  // Non-null assertion is safe: StateGroupId is a closed union and every id has a STATE_GROUPS entry
  // (enforced by the partition-guard test).
  return STATE_GROUPS.find((group) => group.id === id)!;
}

export function groupRankFor(state: IssueState): number {
  // Non-null assertion is safe: every IssueState is assigned to exactly one group in STATE_GROUPS.
  // The partition-guard test in issue-lifecycle.model.spec.ts enforces totality.
  return groupRankMap.get(state)!;
}

// Declarative within-group tier ordering. Each nested array is a peer tier;
// states in the same tier have equal rank. Tier index = rank.
export const WITHIN_GROUP_ORDER: Readonly<Record<StateGroupId, readonly (readonly IssueState[])[]>> = {
  in_progress: [
    ['in_progress', 'revision_in_progress', 'triage_in_progress'],
  ],
  needs_attention: [
    [
      'review',
      'awaiting_input',
      'unchanged',
      'failed',
      'continuable_failed',
      'revision_failed',
      'triage_failed',
      'publish_failed',
      'publish_blocked',
      'branch_diverged',
    ],
  ],
  waiting: [
    ['queued', 'revision_queued', 'continuation_queued', 'triage_queued'],
    ['detected', 'rebase_pending', 'checks_pending'],
    ['blocked'],
  ],
  resolved: [
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
