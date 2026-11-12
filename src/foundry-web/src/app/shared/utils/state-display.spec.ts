import { STATE_ARIA_LABELS, STATE_CSS_CLASSES, STATE_LABELS, cardAccentFor, resolveStateDisplay } from './state-display';
import { RETRIAGEABLE_STATES, RETRYABLE_STATES, WORKING_STATES } from './issue-state';
import type { IssueState } from './issue-state';
import type { RevisionTriggerKind } from './revision-trigger-kind';

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

describe('publish state display (#787)', () => {
  it('should give publish_failed its label, aria label and badge class', () => {
    // Arrange / Act / Assert
    expect(STATE_LABELS.publish_failed).toBe('PUBLISH FAILED');
    expect(STATE_ARIA_LABELS.publish_failed).toBe('publish failed');
    expect(STATE_CSS_CLASSES.publish_failed).toBe('badge--publish-failed');
  });

  it('should give branch_diverged its label, aria label and badge class', () => {
    // Arrange / Act / Assert
    expect(STATE_LABELS.branch_diverged).toBe('BRANCH DIVERGED');
    expect(STATE_ARIA_LABELS.branch_diverged).toBe('branch diverged');
    expect(STATE_CSS_CLASSES.branch_diverged).toBe('badge--branch-diverged');
  });

  it('should make branch_diverged retryable through Retry Issue', () => {
    // Arrange / Act / Assert
    expect(RETRYABLE_STATES.has('branch_diverged')).toBe(true);
  });

  it('should not make publish_failed retryable through Retry Issue, which would abandon the unpublished run', () => {
    // Arrange / Act / Assert
    expect(RETRYABLE_STATES.has('publish_failed')).toBe(false);
  });

  it('should give neither publish state a card accent', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('publish_failed')).toBeNull();
    expect(cardAccentFor('branch_diverged')).toBeNull();
  });
});

describe('secret-blocked state display (#788)', () => {
  it('should give publish_blocked the SECRET DETECTED label, aria label and badge class', () => {
    // Arrange / Act / Assert
    expect(STATE_LABELS.publish_blocked).toBe('SECRET DETECTED');
    expect(STATE_ARIA_LABELS.publish_blocked).toBe('secret detected, nothing pushed');
    expect(STATE_CSS_CLASSES.publish_blocked).toBe('badge--publish-blocked');
  });

  it('should resolve publish_blocked to the SECRET DETECTED display', () => {
    // Arrange / Act
    const display = resolveStateDisplay('publish_blocked');

    // Assert
    expect(display).toEqual({
      label: 'SECRET DETECTED',
      ariaLabel: 'secret detected, nothing pushed',
      cssClass: 'badge--publish-blocked',
    });
  });

  it('should not make publish_blocked retryable through Retry Issue', () => {
    // Arrange / Act / Assert
    expect(RETRYABLE_STATES.has('publish_blocked')).toBe(false);
  });

  it('should give publish_blocked no card accent', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('publish_blocked')).toBeNull();
  });

  it('should show RUN DISCARDED for a failed issue whose run was discarded', () => {
    // Arrange / Act
    const display = resolveStateDisplay('failed', { failureClassification: 'run_discarded' });

    // Assert
    expect(display.label).toBe('RUN DISCARDED');
    expect(display.ariaLabel).toBe('run discarded');
    expect(display.cssClass).toBe('badge--failure-cancelled');
  });
});

describe('resolveStateDisplay', () => {
  const triggerCases: { kind: RevisionTriggerKind; state: IssueState; label: string; ariaLabel: string }[] = [
    { kind: 'rebase_conflict', state: 'revision_queued', label: 'CONFLICT QUEUED', ariaLabel: 'conflict queued' },
    { kind: 'rebase_conflict', state: 'revision_in_progress', label: 'RESOLVING CONFLICTS', ariaLabel: 'resolving conflicts' },
    { kind: 'rebase_conflict', state: 'revision_failed', label: 'CONFLICT FAILED', ariaLabel: 'conflict failed' },
    { kind: 'failed_checks', state: 'revision_queued', label: 'CHECKS FIX QUEUED', ariaLabel: 'checks fix queued' },
    { kind: 'failed_checks', state: 'revision_in_progress', label: 'FIXING CHECKS', ariaLabel: 'fixing checks' },
    { kind: 'failed_checks', state: 'revision_failed', label: 'CHECKS FIX FAILED', ariaLabel: 'checks fix failed' },
    { kind: 'input_provided', state: 'revision_queued', label: 'RESUME QUEUED', ariaLabel: 'resume queued' },
    { kind: 'input_provided', state: 'revision_in_progress', label: 'RESUMING', ariaLabel: 'resuming' },
    { kind: 'input_provided', state: 'revision_failed', label: 'RESUME FAILED', ariaLabel: 'resume failed' },
    { kind: 'review_feedback', state: 'revision_queued', label: 'REV QUEUED', ariaLabel: 'revision queued' },
    { kind: 'review_feedback', state: 'revision_in_progress', label: 'REV IN PROGRESS', ariaLabel: 'revision in progress' },
    { kind: 'review_feedback', state: 'revision_failed', label: 'REV FAILED', ariaLabel: 'revision failed' },
  ];

  for (const c of triggerCases) {
    it(`should resolve "${c.label}" with the state's badge class for ${c.state} triggered by ${c.kind}`, () => {
      // Arrange / Act
      const display = resolveStateDisplay(c.state, { revisionTriggerKind: c.kind });

      // Assert
      expect(display).toEqual({ label: c.label, ariaLabel: c.ariaLabel, cssClass: STATE_CSS_CLASSES[c.state] });
    });
  }

  const revisionStates: IssueState[] = ['revision_queued', 'revision_in_progress', 'revision_failed'];

  for (const state of revisionStates) {
    for (const kind of [null, undefined, 'some_future_trigger']) {
      it(`should fall back to the state display for ${state} when the trigger kind is ${String(kind)}`, () => {
        // Arrange / Act
        const display = resolveStateDisplay(state, { revisionTriggerKind: kind as RevisionTriggerKind | null | undefined });

        // Assert
        expect(display).toEqual({
          label: STATE_LABELS[state],
          ariaLabel: STATE_ARIA_LABELS[state],
          cssClass: STATE_CSS_CLASSES[state],
        });
      });
    }
  }

  it('should fall back to the state display when called without options', () => {
    // Arrange / Act
    const display = resolveStateDisplay('in_progress');

    // Assert
    expect(display).toEqual({ label: 'IN PROGRESS', ariaLabel: 'in progress', cssClass: 'badge--in-progress' });
  });

  it('should ignore a trigger kind on a non-revision state', () => {
    // Arrange / Act
    const display = resolveStateDisplay('awaiting_input', { revisionTriggerKind: 'rebase_conflict' });

    // Assert
    expect(display).toEqual({ label: 'AWAITING INPUT', ariaLabel: 'awaiting input', cssClass: 'badge--awaiting-input' });
  });

  for (const state of ['failed', 'continuable_failed'] as IssueState[]) {
    it(`should resolve the failure category display for ${state} with a known failure classification`, () => {
      // Arrange / Act
      const display = resolveStateDisplay(state, { failureClassification: 'credits_exhausted' });

      // Assert
      expect(display).toEqual({
        label: 'CREDITS EXHAUSTED',
        ariaLabel: 'credits exhausted',
        cssClass: 'badge--failure-credits-exhausted',
      });
    });
  }

  it('should fall back to the state display for failed with an unknown failure classification', () => {
    // Arrange / Act
    const display = resolveStateDisplay('failed', { failureClassification: 'some_unknown_category' });

    // Assert
    expect(display).toEqual({ label: 'FAILED', ariaLabel: 'failed', cssClass: 'badge--failed' });
  });

  it('should show the trigger label for revision_failed even with a failure classification', () => {
    // Arrange / Act
    const display = resolveStateDisplay('revision_failed', {
      failureClassification: 'credits_exhausted',
      revisionTriggerKind: 'rebase_conflict',
    });

    // Assert
    expect(display).toEqual({ label: 'CONFLICT FAILED', ariaLabel: 'conflict failed', cssClass: 'badge--revision-failed' });
  });

  it('should ignore a failure classification on a non-failed state', () => {
    // Arrange / Act
    const display = resolveStateDisplay('triage_failed', { failureClassification: 'non_zero_exit' });

    // Assert
    expect(display).toEqual({ label: 'TRIAGE FAILED', ariaLabel: 'triage failed', cssClass: 'badge--triage-failed' });
  });
});

describe('triage_blocked display (ADR 0102)', () => {
  it('should give triage_blocked its label, aria label and badge class', () => {
    // Arrange / Act / Assert
    expect(STATE_LABELS.triage_blocked).toBe('TRIAGE BLOCKED');
    expect(STATE_ARIA_LABELS.triage_blocked).toBe('triage blocked');
    expect(STATE_CSS_CLASSES.triage_blocked).toBe('badge--triage-blocked');
  });

  it('should give triage_blocked no card accent', () => {
    // Arrange / Act / Assert
    expect(cardAccentFor('triage_blocked')).toBeNull();
  });

  it('should offer no operator action set for triage_blocked', () => {
    // Arrange / Act / Assert
    expect(WORKING_STATES.has('triage_blocked')).toBe(false);
    expect(RETRYABLE_STATES.has('triage_blocked')).toBe(false);
    expect(RETRIAGEABLE_STATES.has('triage_blocked')).toBe(false);
  });
});
