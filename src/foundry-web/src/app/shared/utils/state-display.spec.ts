import { STATE_ARIA_LABELS, STATE_CSS_CLASSES, STATE_LABELS, cardAccentFor } from './state-display';
import { RETRIAGEABLE_STATES, RETRYABLE_STATES, WORKING_STATES } from './issue-state';
import type { IssueState } from './issue-state';

describe('cardAccentFor', () => {
  // Working states → 'working'
  it('should return "working" for in_progress', () => {
    // Arrange
    const state: IssueState = 'in_progress';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('working');
  });

  it('should return "working" for revision_in_progress', () => {
    // Arrange
    const state: IssueState = 'revision_in_progress';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('working');
  });

  // Ready state → 'ready'
  it('should return "ready" for review', () => {
    // Arrange
    const state: IssueState = 'review';

    // Act
    const result = cardAccentFor(state);

    // Assert
    expect(result).toBe('ready');
  });

  // All other 10 states → null
  it('should return null for detected', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('detected')).toBeNull();
  });

  it('should return null for queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('queued')).toBeNull();
  });

  it('should return null for blocked', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('blocked')).toBeNull();
  });

  it('should return null for unchanged', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('unchanged')).toBeNull();
  });

  it('should return null for failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('failed')).toBeNull();
  });

  it('should return null for continuable_failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('continuable_failed')).toBeNull();
  });

  it('should return null for continuation_queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('continuation_queued')).toBeNull();
  });

  it('should return null for completed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('completed')).toBeNull();
  });

  it('should return null for revision_queued', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('revision_queued')).toBeNull();
  });

  it('should return null for revision_failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('revision_failed')).toBeNull();
  });

  it('should return null for checks_pending', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('checks_pending')).toBeNull();
  });

  it('should return null for rebase_pending', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('rebase_pending')).toBeNull();
  });
});

describe('triage state display', () => {
  it('should give the three triage states their labels, aria labels and badge classes', () => {
    // Arrange / Act / Assert
    expect(STATE_LABELS.triage_queued).toBe('TRIAGE QUEUED');
    expect(STATE_LABELS.triage_in_progress).toBe('TRIAGING');
    expect(STATE_LABELS.triage_failed).toBe('TRIAGE FAILED');
    expect(STATE_ARIA_LABELS.triage_queued).toBe('triage queued');
    expect(STATE_ARIA_LABELS.triage_in_progress).toBe('triaging');
    expect(STATE_ARIA_LABELS.triage_failed).toBe('triage failed');
    expect(STATE_CSS_CLASSES.triage_queued).toBe('badge--triage-queued');
    expect(STATE_CSS_CLASSES.triage_in_progress).toBe('badge--triage-in-progress');
    expect(STATE_CSS_CLASSES.triage_failed).toBe('badge--triage-failed');
  });

  it('should return "working" for triage_in_progress', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('triage_in_progress')).toBe('working');
  });

  it('should return null for triage_queued and triage_failed', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('triage_queued')).toBeNull();
    expect(cardAccentFor('triage_failed')).toBeNull();
  });
});

describe('triage state sets', () => {
  it('should treat triage_in_progress as a working state', () => {
    // Arrange / Act / Assert
    expect(WORKING_STATES.has('triage_in_progress')).toBe(true);
  });

  it('should not make triage_failed retryable through Retry Issue', () => {
    // Arrange / Act / Assert
    expect(RETRYABLE_STATES.has('triage_failed')).toBe(false);
  });

  it('should allow re-triage only from queued and blocked', () => {
    // Arrange / Act / Assert
    expect([...RETRIAGEABLE_STATES].sort()).toEqual(['blocked', 'queued']);
  });
});
