import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { RailStepView, railGroups, railView, stepStatusText } from './lifecycle-rail.model';
import { sentenceCase, stepCopy } from './lifecycle-steps';

function step(
  token: string,
  status: LifecycleStep['status'],
  overrides: Partial<LifecycleStep> = {},
): LifecycleStep {
  return { step: token, actor: 'code', status, phase: null, ...overrides };
}

// The full path of a merge-on repository, the issue implementing.
const IMPLEMENTING: readonly LifecycleStep[] = [
  step('detected', 'done'),
  step('triage_queued', 'done'),
  step('triage', 'done', { actor: 'claude' }),
  step('queued', 'done'),
  step('implement', 'current', { actor: 'claude', phase: 'running' }),
  step('publish', 'upcoming'),
  step('rebase', 'upcoming'),
  step('checks', 'upcoming'),
  step('review_queued', 'upcoming'),
  step('review', 'upcoming', { actor: 'claude' }),
  step('your_review', 'skipped', { actor: 'person' }),
  step('merge', 'upcoming'),
  step('done', 'upcoming'),
];

function withStatus(
  lifecycle: readonly LifecycleStep[],
  token: string,
  changes: Partial<LifecycleStep>,
): LifecycleStep[] {
  return lifecycle.map((s) => (s.step === token ? { ...s, ...changes } : s));
}

describe('stepCopy', () => {
  it('labels a known step from the catalogue', () => {
    expect(stepCopy('your_review', 'triage')).toMatchObject({
      label: 'Your review',
      stage: 'merge',
    });
  });

  it('makes an unknown token a node in the previous stage, labelled in sentence case', () => {
    expect(stepCopy('mark_ready', 'merge')).toEqual({
      label: 'Mark ready',
      kind: 'node',
      stage: 'merge',
      doing: 'Mark ready',
    });
  });

  it('sentence-cases a token', () => {
    expect(sentenceCase('mark_ready')).toBe('Mark ready');
  });
});

describe('railView', () => {
  it('shows every served step in order', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, null);

    // Assert
    expect(view.steps.map((s) => s.step)).toEqual(IMPLEMENTING.map((s) => s.step));
  });

  it('gives nodes their own column and puts each wait on the connector before the next node', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, null);

    // Assert
    const columns = Object.fromEntries(view.steps.map((s) => [s.step, s.column]));
    expect(columns).toMatchObject({
      detected: 1,
      triage_queued: 2,
      triage: 3,
      queued: 4,
      implement: 5,
      publish: 7,
    });
    expect(view.columns.split(' auto').length).toBe(
      view.steps.filter((s) => s.kind === 'node').length,
    );
  });

  it('brackets the steps by stage', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, null);

    // Assert
    expect(view.brackets.map((b) => [b.label, b.from, b.to])).toEqual([
      ['Triage', 1, 4],
      ['Implement', 4, 12],
      ['Review', 12, 14],
      ['Merge', 15, 20],
    ]);
  });

  it('dims a stage whose steps are all off', () => {
    // Arrange
    const reviewOff = withStatus(
      withStatus(IMPLEMENTING, 'review_queued', { status: 'off' }),
      'review',
      {
        status: 'off',
      },
    );

    // Act
    const view = railView(reviewOff, 1, null);

    // Assert
    expect(view.brackets.find((b) => b.stage === 'review')?.dim).toBe(true);
    expect(view.steps.find((s) => s.step === 'review')?.word).toBe('off');
  });

  it('marks the current step with its actor, status and phase for a screen reader', () => {
    // Act
    const implement = railView(IMPLEMENTING, 1, null).steps.find((s) => s.step === 'implement');

    // Assert
    expect(implement?.ariaLabel).toBe('Implement, by Claude, current, running');
  });

  it.each([
    ['off', 'Rebase, by Foundry code, off for this repository'],
    ['skipped', 'Rebase, by Foundry code, skipped'],
    ['upcoming', 'Rebase, by Foundry code, upcoming'],
  ] as const)('reads a %s step as "%s"', (status, label) => {
    // Act
    const view = railView(withStatus(IMPLEMENTING, 'rebase', { status }), 1, null);

    // Assert
    expect(view.steps.find((s) => s.step === 'rebase')?.ariaLabel).toBe(label);
  });

  it('badges the Implement node with the round past the first', () => {
    // Act
    const implement = railView(IMPLEMENTING, 2, null).steps.find((s) => s.step === 'implement');

    // Assert
    expect(implement?.badge).toBe(2);
    expect(implement?.ariaLabel).toContain('Implement, ×2');
  });

  it('shows no badge in the first round', () => {
    expect(
      railView(IMPLEMENTING, 1, null).steps.find((s) => s.step === 'implement')?.badge,
    ).toBeNull();
  });

  it('shows no badge on an upcoming Implement node', () => {
    // Arrange
    const queued = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'upcoming', phase: null }),
      'queued',
      {
        status: 'current',
        phase: 'waiting',
      },
    );

    // Act / Assert
    expect(railView(queued, 2, null).steps.find((s) => s.step === 'implement')?.badge).toBeNull();
  });

  it.each([
    ['claude', 'running', 'claude', 'arc'],
    ['code', 'running', 'code', 'dashed'],
    ['claude', 'preparing', 'code', 'dashed'],
    ['code', 'retrying', 'code', 'dashed'],
    ['person', 'needs_you', 'person', 'solid'],
    ['claude', 'failed', 'failed', 'solid'],
    ['code', 'waiting', 'code', null],
  ] as const)(
    'draws a current %s step in phase %s with tone %s and a %s ring',
    (actor, phase, tone, ring) => {
      // Act
      const view = railView(withStatus(IMPLEMENTING, 'implement', { actor, phase }), 1, null);

      // Assert
      expect(view.steps.find((s) => s.step === 'implement')).toMatchObject({ tone, ring });
    },
  );

  it('draws the finished issue as a check on Done', () => {
    // Arrange
    const completed = IMPLEMENTING.map((s) =>
      s.step === 'done'
        ? { ...s, status: 'current' as const }
        : { ...s, status: 'done' as const, phase: null },
    );

    // Act
    const done = railView(completed, 1, null).steps.find((s) => s.step === 'done');

    // Assert
    expect(done).toMatchObject({ tone: 'done', glyph: 'check', ring: null });
  });

  it('fills a connector whose next step is done or current and half-fills a current wait', () => {
    // Arrange
    const queued = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'upcoming', phase: null }),
      'queued',
      {
        status: 'current',
        phase: 'waiting',
      },
    );

    // Act
    const view = railView(queued, 1, null);

    // Assert
    expect(view.connectors.slice(0, 4).map((c) => c.fill)).toEqual([
      'full',
      'half',
      'none',
      'none',
    ]);
  });

  it('keeps a step it has no copy for, as a node with its served actor and status', () => {
    // Arrange
    const lifecycle = [
      ...IMPLEMENTING.slice(0, 5),
      step('mark_ready', 'upcoming', { actor: 'person' }),
    ];

    // Act
    const view = railView(lifecycle, 1, null);

    // Assert
    expect(view.steps.at(-1)).toMatchObject({
      label: 'Mark ready',
      kind: 'node',
      stage: 'implement',
      actor: 'person',
      status: 'upcoming',
    });
  });

  it('moves nothing on the first render', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, null);

    // Assert
    expect(view.steps.every((s) => s.motion === null)).toBe(true);
    expect(view.connectors.every((c) => !c.grow)).toBe(true);
  });

  it('pops the new current step, settles the newly done one and grows the connector to it', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      {
        status: 'current',
        phase: 'running',
      },
    );

    // Act
    const view = railView(publishing, 1, IMPLEMENTING);

    // Assert
    expect(view.steps.find((s) => s.step === 'implement')?.motion).toBe('settle');
    expect(view.steps.find((s) => s.step === 'publish')?.motion).toBe('pop');
    expect(view.connectors.filter((c) => c.grow).map((c) => c.to)).toEqual([7]);
  });

  it('shakes a current step that just failed', () => {
    // Act
    const view = railView(
      withStatus(IMPLEMENTING, 'implement', { phase: 'failed' }),
      1,
      IMPLEMENTING,
    );

    // Assert
    expect(view.steps.find((s) => s.step === 'implement')?.motion).toBe('shake');
  });

  it('pops Done when the issue completes', () => {
    // Arrange
    const completed = IMPLEMENTING.map((s) =>
      s.step === 'done'
        ? { ...s, status: 'current' as const }
        : { ...s, status: 'done' as const, phase: null },
    );

    // Act
    const view = railView(completed, 1, IMPLEMENTING);

    // Assert
    expect(view.steps.find((s) => s.step === 'done')?.motion).toBe('pop');
  });

  it('moves nothing when the lifecycle did not change', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, IMPLEMENTING);

    // Assert
    expect(view.steps.every((s) => s.motion === null)).toBe(true);
  });
});

describe('railGroups', () => {
  it('groups the steps by stage, in order', () => {
    // Act
    const groups = railGroups(railView(IMPLEMENTING, 1, null));

    // Assert
    expect(groups.map((g) => [g.label, g.steps.map((s) => s.step)])).toEqual([
      ['Triage', ['detected', 'triage_queued', 'triage']],
      ['Implement', ['queued', 'implement', 'publish', 'rebase', 'checks']],
      ['Review', ['review_queued', 'review']],
      ['Merge', ['your_review', 'merge', 'done']],
    ]);
  });

  it('marks the stage holding the current step and dims a stage whose steps are all off', () => {
    // Arrange
    const lifecycle = IMPLEMENTING.map((s) =>
      s.step === 'review_queued' || s.step === 'review' ? { ...s, status: 'off' as const } : s,
    );

    // Act
    const groups = railGroups(railView(lifecycle, 1, null));

    // Assert
    expect(groups.map((g) => [g.stage, g.current, g.dim])).toEqual([
      ['triage', false, false],
      ['implement', true, false],
      ['review', false, true],
      ['merge', false, false],
    ]);
  });
});

describe('vertical line', () => {
  it("fills the line down to a done or current step and draws none after a stage's last step", () => {
    // Act
    const groups = railGroups(railView(IMPLEMENTING, 1, null));

    // Assert
    expect(groups[0].steps.map((s) => s.line)).toEqual(['full', 'full', null]);
    expect(groups[1].steps.map((s) => s.line)).toEqual(['full', 'none', 'none', 'none', null]);
  });

  it('grows the line once when the step below it becomes current', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      { status: 'current', phase: 'running' },
    );

    // Act
    const view = railView(publishing, 1, IMPLEMENTING);

    // Assert
    expect(view.steps.filter((s) => s.lineGrow).map((s) => s.step)).toEqual(['implement']);
  });

  it('grows no line on the first render', () => {
    // Act
    const view = railView(IMPLEMENTING, 1, null);

    // Assert
    expect(view.steps.some((s) => s.lineGrow)).toBe(false);
  });
});

describe('actor words', () => {
  it('names who runs each step', () => {
    // Act
    const view = railView(
      [
        step('detected', 'done'),
        step('triage', 'done', { actor: 'claude' }),
        step('your_review', 'upcoming', { actor: 'person' }),
      ],
      1,
      null,
    );

    // Assert
    expect(view.steps.map((s) => s.actorWord)).toEqual(['Foundry code', 'Claude', 'You']);
  });
});

describe('stepStatusText', () => {
  function only(lifecycleStep: LifecycleStep): RailStepView {
    return railView([lifecycleStep], 1, null).steps[0];
  }

  it.each([
    ['done', 'Done'],
    ['upcoming', 'Upcoming'],
    ['off', 'Off for this repository'],
    ['skipped', 'Skipped'],
  ] as const)('reads a %s step as "%s" with no elapsed time', (status, text) => {
    expect(stepStatusText(only(step('publish', status)), '12m')).toBe(text);
  });

  it.each([
    ['running', 'Running · 12m'],
    ['preparing', 'Preparing · 12m'],
    ['retrying', 'Retrying · 12m'],
    ['needs_you', 'Needs you · 12m'],
    ['failed', 'Failed · 12m'],
    ['waiting', 'Waiting · 12m'],
  ] as const)('reads the current step in its %s phase as "%s"', (phase, text) => {
    expect(stepStatusText(only(step('implement', 'current', { phase })), '12m')).toBe(text);
  });

  it('reads a current step with no phase as in progress', () => {
    expect(stepStatusText(only(step('implement', 'current')), '5m')).toBe('In progress · 5m');
  });

  it("leaves the elapsed time out when the step's start is unknown", () => {
    expect(stepStatusText(only(step('implement', 'current', { phase: 'running' })), null)).toBe(
      'Running',
    );
  });
});
