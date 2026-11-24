import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { BarSegmentView, barView } from './lifecycle-bar.model';

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
  step('review', 'off', { actor: 'claude' }),
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

function segment(segments: readonly BarSegmentView[], token: string): BarSegmentView {
  return segments.find((s) => s.step === token)!;
}

describe('barView', () => {
  it('gives one segment per rail node, the queue waits folded away', () => {
    // Act
    const segments = barView(IMPLEMENTING, null);

    // Assert
    expect(segments.map((s) => s.step)).toEqual([
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
    ]);
  });

  it('shows a done step as done', () => {
    // Act
    const triage = segment(barView(IMPLEMENTING, null), 'triage');

    // Assert
    expect(triage).toMatchObject({ status: 'done', queued: false });
  });

  it('shows an upcoming step as upcoming', () => {
    // Act
    const publish = segment(barView(IMPLEMENTING, null), 'publish');

    // Assert
    expect(publish).toMatchObject({ status: 'upcoming', queued: false });
  });

  it('shows a step off for the repository as off', () => {
    // Act
    const review = segment(barView(IMPLEMENTING, null), 'review');

    // Assert
    expect(review.status).toBe('off');
  });

  it('shows a skipped step as skipped', () => {
    // Act
    const yourReview = segment(barView(IMPLEMENTING, null), 'your_review');

    // Assert
    expect(yourReview.status).toBe('skipped');
  });

  it("colours a current step Claude runs in the working accent, with the step's phase", () => {
    // Act
    const implement = segment(barView(IMPLEMENTING, null), 'implement');

    // Assert
    expect(implement).toMatchObject({
      status: 'current',
      phase: 'running',
      tone: 'var(--fd-accent-working)',
    });
  });

  it('colours a current step Foundry code runs as a link', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      { status: 'current', phase: 'running' },
    );

    // Act
    const publish = segment(barView(publishing, null), 'publish');

    // Assert
    expect(publish.tone).toBe('var(--fd-text-link)');
  });

  it('colours a current step that needs a person as a warning', () => {
    // Arrange
    const needsYou = withStatus(IMPLEMENTING, 'implement', { phase: 'needs_you' });

    // Act
    const implement = segment(barView(needsYou, null), 'implement');

    // Assert
    expect(implement.tone).toBe('var(--fd-text-warning)');
  });

  it('colours a failed current step as an error', () => {
    // Arrange
    const failed = withStatus(IMPLEMENTING, 'implement', { phase: 'failed' });

    // Act
    const implement = segment(barView(failed, null), 'implement');

    // Assert
    expect(implement.tone).toBe('var(--fd-text-error)');
  });

  it('marks the node after a current queued wait as queued, in its actor tone', () => {
    // Arrange
    const queued = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'upcoming', phase: null }),
      'queued',
      { status: 'current', phase: 'waiting' },
    );

    // Act
    const implement = segment(barView(queued, null), 'implement');

    // Assert
    expect(implement).toMatchObject({
      status: 'upcoming',
      queued: true,
      tone: 'var(--fd-accent-working)',
    });
  });

  it('marks the Triage node queued while the issue waits for triage', () => {
    // Arrange
    const waiting: readonly LifecycleStep[] = [
      step('detected', 'done'),
      step('triage_queued', 'current', { phase: 'waiting' }),
      step('triage', 'upcoming', { actor: 'claude' }),
      step('queued', 'upcoming'),
      step('implement', 'upcoming', { actor: 'claude' }),
    ];

    // Act
    const segments = barView(waiting, null);

    // Assert
    expect(segments.filter((s) => s.queued).map((s) => s.step)).toEqual(['triage']);
    expect(segment(segments, 'triage').tone).toBe('var(--fd-accent-working)');
  });

  it('moves nothing on the first render', () => {
    // Act
    const segments = barView(IMPLEMENTING, null);

    // Assert
    expect(segments.filter((s) => s.motion !== null)).toEqual([]);
  });

  it('fills a step that became done and settles the step that became current, once', () => {
    // Arrange
    const publishing = withStatus(
      withStatus(IMPLEMENTING, 'implement', { status: 'done', phase: null }),
      'publish',
      { status: 'current', phase: 'running' },
    );

    // Act
    const segments = barView(publishing, IMPLEMENTING);

    // Assert
    expect(segments.filter((s) => s.motion !== null).map((s) => [s.step, s.motion])).toEqual([
      ['implement', 'fill'],
      ['publish', 'settle'],
    ]);
  });

  it('shakes a step that just failed', () => {
    // Arrange
    const failed = withStatus(IMPLEMENTING, 'implement', { phase: 'failed' });

    // Act
    const implement = segment(barView(failed, IMPLEMENTING), 'implement');

    // Assert
    expect(implement.motion).toBe('shake');
  });

  it('moves nothing when the lifecycle is unchanged', () => {
    // Act
    const segments = barView(IMPLEMENTING, IMPLEMENTING);

    // Assert
    expect(segments.filter((s) => s.motion !== null)).toEqual([]);
  });
});
