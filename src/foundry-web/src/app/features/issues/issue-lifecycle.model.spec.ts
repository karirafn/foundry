import { ACTIVE_STATES, RESOLVED_STATES, STATE_GROUPS, UNGROUPED_RANK, groupRankFor, isResolvedState } from './issue-lifecycle.model';
import { IssueState } from './issue.model';

// All lifecycle states — ineligible is an overlay, not a lifecycle state.
const ALL_LIFECYCLE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
  'detected',
  'queued',
  'blocked',
  'in_progress',
  'review',
  'unchanged',
  'failed',
  'continuable_failed',
  'continuation_queued',
  'completed',
  'revision_queued',
  'revision_in_progress',
  'revision_failed',
]);

describe('issue-lifecycle model', () => {
  // Cycle 1: ACTIVE_STATES and RESOLVED_STATES match backend registry exactly
  it('should include all active states from the backend IssueStateRegistry', () => {
    // Arrange
    const expectedActive: ReadonlySet<IssueState> = new Set<IssueState>([
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

    // Act / Assert
    expect(ACTIVE_STATES.size).toBe(expectedActive.size);
    for (const state of expectedActive) {
      expect(ACTIVE_STATES.has(state)).toBe(true);
    }
  });

  it('should include all resolved states from the backend IssueStateRegistry', () => {
    // Arrange
    const expectedResolved: ReadonlySet<IssueState> = new Set<IssueState>([
      'completed',
      'unchanged',
    ]);

    // Act / Assert
    expect(RESOLVED_STATES.size).toBe(expectedResolved.size);
    for (const state of expectedResolved) {
      expect(RESOLVED_STATES.has(state)).toBe(true);
    }
  });

  it('should not include ineligible in ACTIVE_STATES', () => {
    // Arrange / Act / Assert
    expect(ACTIVE_STATES.has('ineligible' as IssueState)).toBe(false);
  });

  it('should not include ineligible in RESOLVED_STATES', () => {
    // Arrange / Act / Assert
    expect(RESOLVED_STATES.has('ineligible' as IssueState)).toBe(false);
  });

  // Cycle 2: isResolvedState predicate
  it('should return true for resolved states', () => {
    // Arrange / Act / Assert
    expect(isResolvedState('completed')).toBe(true);
    expect(isResolvedState('unchanged')).toBe(true);
  });

  it('should return false for active states', () => {
    // Arrange / Act / Assert
    expect(isResolvedState('in_progress')).toBe(false);
    expect(isResolvedState('detected')).toBe(false);
    expect(isResolvedState('failed')).toBe(false);
  });

  // Cycle 3: STATE_GROUPS completeness and partition guard
  it('should define exactly four semantic groups', () => {
    // Arrange / Act / Assert
    expect(STATE_GROUPS.length).toBe(4);
  });

  it('should have the four expected group labels', () => {
    // Arrange
    const labels = STATE_GROUPS.map(g => g.label);

    // Act / Assert
    expect(labels).toContain('In progress');
    expect(labels).toContain('Waiting');
    expect(labels).toContain('Needs attention');
    expect(labels).toContain('Resolved');
  });

  it('should cover every lifecycle state (except ineligible) in exactly one group', () => {
    // Arrange
    const seenStates = new Map<IssueState, string>();

    // Act
    for (const group of STATE_GROUPS) {
      for (const state of group.states) {
        if (seenStates.has(state)) {
          throw new Error(`State '${state}' appears in more than one group`);
        }
        seenStates.set(state, group.label);
      }
    }

    // Assert — every lifecycle state appears in exactly one group
    const missingStates: IssueState[] = [];
    for (const state of ALL_LIFECYCLE_STATES) {
      if (!seenStates.has(state)) {
        missingStates.push(state);
      }
    }
    expect(missingStates).toEqual([]);
  });

  it('should have group states that form the union of ACTIVE_STATES and RESOLVED_STATES', () => {
    // Arrange
    const allGroupStates = new Set<IssueState>(
      STATE_GROUPS.flatMap(g => g.states)
    );
    const expectedUnion = new Set<IssueState>([...ACTIVE_STATES, ...RESOLVED_STATES]);

    // Act / Assert
    expect(allGroupStates.size).toBe(expectedUnion.size);
    const missingFromGroups = [...expectedUnion].filter(s => !allGroupStates.has(s));
    expect(missingFromGroups).toEqual([]);
  });

  it('should place In progress group first', () => {
    // Arrange / Act / Assert
    expect(STATE_GROUPS[0].label).toBe('In progress');
  });

  it('should place Resolved group last', () => {
    // Arrange / Act / Assert
    expect(STATE_GROUPS[STATE_GROUPS.length - 1].label).toBe('Resolved');
  });

  it('should place Needs attention before Waiting', () => {
    // Arrange
    const needsAttentionIndex = STATE_GROUPS.findIndex(g => g.label === 'Needs attention');
    const waitingIndex = STATE_GROUPS.findIndex(g => g.label === 'Waiting');

    // Act / Assert
    expect(needsAttentionIndex).toBeLessThan(waitingIndex);
  });

  // Cycle 5: groupRankFor helper
  it('should return the group index for a state in that group', () => {
    // Arrange
    const inProgressIndex = STATE_GROUPS.findIndex(g => g.label === 'In progress');

    // Act / Assert
    expect(groupRankFor('in_progress')).toBe(inProgressIndex);
  });

  it('should rank Needs attention before Waiting', () => {
    // Arrange / Act / Assert
    expect(groupRankFor('review')).toBeLessThan(groupRankFor('queued'));
  });

  it('should return UNGROUPED_RANK for ineligible', () => {
    // Arrange / Act / Assert
    expect(groupRankFor('ineligible')).toBe(UNGROUPED_RANK);
  });

  it('should return the Resolved group index (3) for a Resolved-group state', () => {
    // Arrange
    const resolvedIndex = STATE_GROUPS.findIndex(g => g.label === 'Resolved');

    // Act
    const rank = groupRankFor('completed');

    // Assert
    expect(rank).toBe(resolvedIndex);
    expect(rank).toBe(3);
  });

  it('should return UNGROUPED_RANK that sorts after all defined groups', () => {
    // Arrange / Act / Assert
    for (let i = 0; i < STATE_GROUPS.length; i++) {
      expect(UNGROUPED_RANK).toBeGreaterThan(i);
    }
  });

  // Cycle 4: group → state membership spot checks
  it('should place in_progress, revision_in_progress, continuation_queued in the In progress group', () => {
    // Arrange
    const inProgress = STATE_GROUPS.find(g => g.label === 'In progress');

    // Act / Assert
    expect(inProgress).toBeDefined();
    expect(inProgress!.states).toContain('in_progress');
    expect(inProgress!.states).toContain('revision_in_progress');
    expect(inProgress!.states).toContain('continuation_queued');
  });

  it('should place detected, queued, blocked, revision_queued in the Waiting group', () => {
    // Arrange
    const waiting = STATE_GROUPS.find(g => g.label === 'Waiting');

    // Act / Assert
    expect(waiting).toBeDefined();
    expect(waiting!.states).toContain('detected');
    expect(waiting!.states).toContain('queued');
    expect(waiting!.states).toContain('blocked');
    expect(waiting!.states).toContain('revision_queued');
  });

  it('should place review, failed, continuable_failed, revision_failed in the Needs attention group', () => {
    // Arrange
    const needsAttention = STATE_GROUPS.find(g => g.label === 'Needs attention');

    // Act / Assert
    expect(needsAttention).toBeDefined();
    expect(needsAttention!.states).toContain('review');
    expect(needsAttention!.states).toContain('failed');
    expect(needsAttention!.states).toContain('continuable_failed');
    expect(needsAttention!.states).toContain('revision_failed');
  });

  it('should place completed and unchanged in the Resolved group', () => {
    // Arrange
    const resolved = STATE_GROUPS.find(g => g.label === 'Resolved');

    // Act / Assert
    expect(resolved).toBeDefined();
    expect(resolved!.states).toContain('completed');
    expect(resolved!.states).toContain('unchanged');
  });
});
