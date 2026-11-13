import {
  ACTIVE_STATES,
  RESOLVED_STATES,
  isKnownState,
  isResolvedState,
} from './issue-lifecycle.model';
import { IssueState } from './issue.model';

// All lifecycle states — must match IssueState union exactly.
const ALL_LIFECYCLE_STATES: ReadonlySet<IssueState> = new Set<IssueState>([
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
  'completed',
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

describe('issue-lifecycle model', () => {
  // Cycle 1: ACTIVE_STATES and RESOLVED_STATES match backend registry exactly
  // Contract test: mirror IssueStateRegistry.ActiveMap (server: src/Modules/Issues/.../StateChanges/IssueStateRegistry.cs)
  it('should include all active states from the backend IssueStateRegistry.ActiveMap', () => {
    // Arrange
    const expectedActive: ReadonlySet<IssueState> = new Set<IssueState>([
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

    // Act / Assert
    expect(ACTIVE_STATES.size).toBe(expectedActive.size);
    for (const state of expectedActive) {
      expect(ACTIVE_STATES.has(state)).toBe(true);
    }
  });

  // Contract test: mirror IssueStateRegistry.ResolvedMap (server: src/Modules/Issues/.../StateChanges/IssueStateRegistry.cs)
  it('should include all resolved states from the backend IssueStateRegistry.ResolvedMap', () => {
    // Arrange
    const expectedResolved: ReadonlySet<IssueState> = new Set<IssueState>([
      'completed',
    ]);

    // Act / Assert
    expect(RESOLVED_STATES.size).toBe(expectedResolved.size);
    for (const state of expectedResolved) {
      expect(RESOLVED_STATES.has(state)).toBe(true);
    }
  });

  // Cycle 2: isResolvedState predicate
  it('should return true for resolved states', () => {
    // Arrange / Act / Assert
    expect(isResolvedState('completed')).toBe(true);
  });

  it('should return false for active states', () => {
    // Arrange / Act / Assert
    expect(isResolvedState('in_progress')).toBe(false);
    expect(isResolvedState('detected')).toBe(false);
    expect(isResolvedState('failed')).toBe(false);
    expect(isResolvedState('unchanged')).toBe(false);
  });

  it('should partition every lifecycle state into exactly one of Active and Resolved', () => {
    // Act
    const union = new Set<IssueState>([...ACTIVE_STATES, ...RESOLVED_STATES]);
    const overlap = [...ACTIVE_STATES].filter((state) => RESOLVED_STATES.has(state));

    // Assert
    expect(overlap).toEqual([]);
    expect([...union].sort()).toEqual([...ALL_LIFECYCLE_STATES].sort());
  });

  it('should recognise every lifecycle state as known', () => {
    // Act
    const unknown = [...ALL_LIFECYCLE_STATES].filter((state) => !isKnownState(state));

    // Assert
    expect(unknown).toEqual([]);
  });

  it('should not recognise a state the dashboard does not know', () => {
    // Act / Assert
    expect(isKnownState('archived')).toBe(false);
  });
});

describe('triage and publish states', () => {
  const states: IssueState[] = [
    'triage_queued',
    'triage_in_progress',
    'triage_failed',
    'triage_blocked',
    'publish_failed',
    'publish_blocked',
    'branch_diverged',
  ];

  for (const state of states) {
    it(`should treat ${state} as active, not resolved`, () => {
      // Act / Assert
      expect(ACTIVE_STATES.has(state)).toBe(true);
      expect(isResolvedState(state)).toBe(false);
    });
  }
});
