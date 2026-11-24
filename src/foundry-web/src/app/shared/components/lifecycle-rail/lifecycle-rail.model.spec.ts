import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { RailStepView, foldWaits, railView, stepStatusText } from './lifecycle-rail.model';
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

const NODES = [
  'detected',
  'triage',
  'implement',
  'publish',
  'rebase',
  'checks',
  'review',
  'your_review',
  'merge',
  'done',
];

function withStatus(
  lifecycle: readonly LifecycleStep[],
  token: string,
  changes: Partial<LifecycleStep>,
): LifecycleStep[] {
  return lifecycle.map((s) => (s.step === token ? { ...s, ...changes } : s));
}

// Queued for implement: the wait is current, Implement upcoming.
const QUEUED: readonly LifecycleStep[] = withStatus(
  withStatus(IMPLEMENTING, 'implement', { status: 'upcoming', phase: null }),
  'queued',
  { status: 'current', phase: 'waiting' },
);

describe('stepCopy', () => {
  it('labels a known step from the catalogue', () => {
    expect(stepCopy('your_review')).toMatchObject({ label: 'Your review', kind: 'node' });
  });

  it('keeps the wait labels for the Pipeline phrase', () => {
    expect(stepCopy('queued')).toMatchObject({ label: 'Queued for implement', kind: 'wait' });
  });

  it('makes an unknown token a node, labelled in sentence case', () => {
    expect(stepCopy('mark_ready')).toEqual({
      label: 'Mark ready',
      kind: 'node',
      doing: 'Mark ready',
    });
  });

  it('sentence-cases a token', () => {
    expect(sentenceCase('mark_ready')).toBe('Mark ready');
  });
});

describe('foldWaits', () => {
  it('keeps only the nodes, in served order', () => {
    // Act
    const nodes = foldWaits(IMPLEMENTING, null);

    // Assert
    expect(nodes.map((n) => n.step.step)).toEqual(NODES);
    expect(nodes.some((n) => n.queued)).toBe(false);
  });

  it.each([
    ['triage_queued', 'triage'],
    ['queued', 'implement'],
    ['review_queued', 'review'],
  ])('folds a current %s wait into the %s node after it', (wait, node) => {
    // Arrange
    const lifecycle = IMPLEMENTING.map((s) =>
      s.step === wait
        ? { ...s, status: 'current' as const, phase: 'waiting' as const }
        : { ...s, status: 'upcoming' as const, phase: null },
    );

    // Act
    const nodes = foldWaits(lifecycle, null);

    // Assert
    expect(nodes.filter((n) => n.queued).map((n) => [n.step.step, n.step.status])).toEqual([
      [node, 'upcoming'],
    ]);
  });

  it('drops a current wait with no node after it', () => {
    // Act
    const nodes = foldWaits([step('detected', 'done'), step('queued', 'current')], null);

    // Assert
    expect(nodes.map((n) => [n.step.step, n.queued])).toEqual([['detected', false]]);
  });

  it('leads into each node but the first, full into a done or current node', () => {
    // Act
    const nodes = foldWaits(IMPLEMENTING, null);

    // Assert
    expect(nodes.slice(0, 4).map((n) => n.lead)).toEqual([null, 'full', 'full', 'none']);
  });

  it('half-fills the lead into a queued node', () => {
    // Act
    const nodes = foldWaits(QUEUED, null);

    // Assert
    expect(nodes.slice(1, 4).map((n) => n.lead)).toEqual(['full', 'half', 'none']);
  });

  it('moves nothing on the first render', () => {
    // Act
    const nodes = foldWaits(QUEUED, null);

    // Assert
    expect(nodes.every((n) => n.motion === null && !n.leadGrow)).toBe(true);
  });

  it('pops the folded node and grows its lead once when the wait becomes current', () => {
    // Arrange
    const triaged = withStatus(
      withStatus(QUEUED, 'queued', { status: 'upcoming', phase: null }),
      'triage',
      {
        status: 'current',
        phase: 'running',
      },
    );

    // Act
    const nodes = foldWaits(QUEUED, triaged);

    // Assert
    const implement = nodes.find((n) => n.step.step === 'implement');
    expect(implement).toMatchObject({ queued: true, motion: 'pop', lead: 'half', leadGrow: true });
    expect(nodes.find((n) => n.step.step === 'triage')?.motion).toBe('settle');
  });

  it('grows the lead from half to full when the queued node starts', () => {
    // Act
    const nodes = foldWaits(IMPLEMENTING, QUEUED);

    // Assert
    expect(nodes.filter((n) => n.leadGrow).map((n) => n.step.step)).toEqual(['implement']);
    expect(nodes.find((n) => n.step.step === 'implement')?.motion).toBe('pop');
  });

  it('moves nothing when the lifecycle did not change', () => {
    // Act
    const nodes = foldWaits(QUEUED, QUEUED);

    // Assert
    expect(nodes.every((n) => n.motion === null && !n.leadGrow)).toBe(true);
  });
});

describe('railView', () => {
  it('shows one step per node, with no waits', () => {
    // Act
    const steps = railView(IMPLEMENTING, 1, null);

    // Assert
    expect(steps.map((s) => s.step)).toEqual(NODES);
  });

  it('marks the current step with its actor, status and phase for a screen reader', () => {
    // Act
    const implement = railView(IMPLEMENTING, 1, null).find((s) => s.step === 'implement');

    // Assert
    expect(implement?.ariaLabel).toBe('Implement, by Claude, current, running');
  });

  it('shows a current wait on the node after it, queued', () => {
    // Act
    const implement = railView(QUEUED, 1, null).find((s) => s.step === 'implement');

    // Assert
    expect(implement).toMatchObject({
      status: 'upcoming',
      queued: true,
      ring: 'queued',
      tone: 'claude',
      word: 'queued',
      lead: 'half',
      ariaLabel: 'Implement, by Claude, upcoming, queued',
    });
  });

  it.each([
    ['off', 'Rebase, by Foundry code, off for this repository'],
    ['skipped', 'Rebase, by Foundry code, skipped'],
    ['upcoming', 'Rebase, by Foundry code, upcoming'],
  ] as const)('reads a %s step as "%s"', (status, label) => {
    // Act
    const steps = railView(withStatus(IMPLEMENTING, 'rebase', { status }), 1, null);

    // Assert
    expect(steps.find((s) => s.step === 'rebase')?.ariaLabel).toBe(label);
  });

  it('words a step that is off', () => {
    // Act
    const steps = railView(withStatus(IMPLEMENTING, 'review', { status: 'off' }), 1, null);

    // Assert
    expect(steps.find((s) => s.step === 'review')?.word).toBe('off');
  });

  it('badges the Implement node with the round past the first', () => {
    // Act
    const implement = railView(IMPLEMENTING, 2, null).find((s) => s.step === 'implement');

    // Assert
    expect(implement?.badge).toBe(2);
    expect(implement?.ariaLabel).toContain('Implement, ×2');
  });

  it('shows no badge in the first round', () => {
    expect(railView(IMPLEMENTING, 1, null).find((s) => s.step === 'implement')?.badge).toBeNull();
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
      const steps = railView(withStatus(IMPLEMENTING, 'implement', { actor, phase }), 1, null);

      // Assert
      expect(steps.find((s) => s.step === 'implement')).toMatchObject({ tone, ring });
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
    const done = railView(completed, 1, null).find((s) => s.step === 'done');

    // Assert
    expect(done).toMatchObject({ tone: 'done', glyph: 'check', ring: null });
  });

  it('keeps a step it has no copy for, as a node with its served actor and status', () => {
    // Arrange
    const lifecycle = [
      ...IMPLEMENTING.slice(0, 5),
      step('mark_ready', 'upcoming', { actor: 'person' }),
    ];

    // Act
    const steps = railView(lifecycle, 1, null);

    // Assert
    expect(steps.at(-1)).toMatchObject({
      label: 'Mark ready',
      actor: 'person',
      status: 'upcoming',
    });
  });

  it('pops the new current step and settles the newly done one', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      { status: 'current', phase: 'running' },
    );

    // Act
    const steps = railView(publishing, 1, IMPLEMENTING);

    // Assert
    expect(steps.find((s) => s.step === 'implement')?.motion).toBe('settle');
    expect(steps.find((s) => s.step === 'publish')?.motion).toBe('pop');
    expect(steps.filter((s) => s.leadGrow).map((s) => s.step)).toEqual(['publish']);
  });

  it('shakes a current step that just failed', () => {
    // Act
    const steps = railView(
      withStatus(IMPLEMENTING, 'implement', { phase: 'failed' }),
      1,
      IMPLEMENTING,
    );

    // Assert
    expect(steps.find((s) => s.step === 'implement')?.motion).toBe('shake');
  });

  it('pops Done when the issue completes', () => {
    // Arrange
    const completed = IMPLEMENTING.map((s) =>
      s.step === 'done'
        ? { ...s, status: 'current' as const }
        : { ...s, status: 'done' as const, phase: null },
    );

    // Act
    const steps = railView(completed, 1, IMPLEMENTING);

    // Assert
    expect(steps.find((s) => s.step === 'done')?.motion).toBe('pop');
  });
});

describe('vertical line', () => {
  it('runs a line down to each next node, filled as that node is reached, none after the last', () => {
    // Act
    const steps = railView(QUEUED, 1, null);

    // Assert
    expect(steps.map((s) => s.line)).toEqual([
      'full',
      'half',
      'none',
      'none',
      'none',
      'none',
      'none',
      'none',
      'none',
      null,
    ]);
  });

  it('grows the line once when the step below it becomes current', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      { status: 'current', phase: 'running' },
    );

    // Act
    const steps = railView(publishing, 1, IMPLEMENTING);

    // Assert
    expect(steps.filter((s) => s.lineGrow).map((s) => s.step)).toEqual(['implement']);
  });
});

describe('actor words', () => {
  it('names who runs each step', () => {
    // Act
    const steps = railView(
      [
        step('detected', 'done'),
        step('triage', 'done', { actor: 'claude' }),
        step('your_review', 'upcoming', { actor: 'person' }),
      ],
      1,
      null,
    );

    // Assert
    expect(steps.map((s) => s.actorWord)).toEqual(['Foundry code', 'Claude', 'You']);
  });
});

describe('stepStatusText', () => {
  function only(lifecycleStep: LifecycleStep): RailStepView {
    return railView([lifecycleStep], 1, null)[0];
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

  it("reads a queued node with the wait's elapsed time", () => {
    // Arrange
    const implement = railView(QUEUED, 1, null).find((s) => s.step === 'implement')!;

    // Act / Assert
    expect(stepStatusText(implement, '4m')).toBe('Queued · 4m');
    expect(stepStatusText(implement, null)).toBe('Queued');
  });
});
