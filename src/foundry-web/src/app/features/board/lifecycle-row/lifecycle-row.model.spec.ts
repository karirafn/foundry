import { IssueSummary } from '../../issues/issue.model';
import { LifecycleStep } from '../../issues/lifecycle.model';
import { NeedsItem, StepItem, WorkerItem } from '../board.model';
import { currentStepLabel, isRunningRow, rowStatus } from './lifecycle-row.model';

function issue(current: LifecycleStep | null): IssueSummary {
  return {
    id: 'i',
    issueNumber: 1,
    title: 'T',
    state: 'in_progress',
    repositorySlug: 'acme/api',
    detectedAt: '2026-01-01T00:00:00Z',
    url: 'https://github.com/acme/api/issues/1',
    stage: 'implement',
    needsAction: false,
    awaitsDispatch: false,
    waitsOnBlockers: false,
    runKind: 'fresh',
    repositoryId: 'r',
    enteredStateAt: '2026-01-01T00:00:00Z',
    round: 1,
    ...(current === null ? {} : { lifecycle: [current] }),
  };
}

function worker(current: LifecycleStep | null): WorkerItem {
  return { band: 'workers', issue: issue(current), stage: 'implement', runKind: 'fresh' };
}

function step(current: LifecycleStep | null): StepItem {
  return { band: 'steps', issue: issue(current), stage: 'implement' };
}

const needs: NeedsItem = {
  band: 'needs',
  issue: issue(null),
  stage: 'implement',
  reason: { label: 'Failed', tone: 'error' },
  explanation: 'The run failed',
};

describe('rowStatus', () => {
  it('shows the completion once the row is a ghost', () => {
    expect(rowStatus(worker(null), 'confirm')).toEqual({ kind: 'completed' });
  });

  it('shows why a person is needed on a Needs you row', () => {
    expect(rowStatus(needs, 'live')).toEqual({ kind: 'needs' });
  });

  it('shows the live activity of a running Claude step', () => {
    // Act
    const status = rowStatus(
      worker({ step: 'implement', actor: 'claude', status: 'current', phase: 'running' }),
      'live',
    );

    // Assert
    expect(status).toEqual({ kind: 'activity' });
  });

  it('shows the live activity of a worker row with no lifecycle', () => {
    expect(rowStatus(worker(null), 'live')).toEqual({ kind: 'activity' });
  });

  it('says Foundry prepares the workspace while the run starts', () => {
    // Act
    const status = rowStatus(
      worker({ step: 'implement', actor: 'claude', status: 'current', phase: 'preparing' }),
      'live',
    );

    // Assert
    expect(status).toEqual({
      kind: 'step',
      text: 'Foundry is preparing the workspace',
      clock: 'running',
    });
  });

  it('names the step being retried', () => {
    // Act
    const status = rowStatus(
      step({ step: 'publish', actor: 'code', status: 'current', phase: 'retrying' }),
      'live',
    );

    // Assert
    expect(status).toEqual({ kind: 'step', text: 'Retrying publish', clock: 'running' });
  });

  it('says what a running code step is doing', () => {
    // Act
    const status = rowStatus(
      step({ step: 'rebase', actor: 'code', status: 'current', phase: 'running' }),
      'live',
    );

    // Assert
    expect(status).toEqual({
      kind: 'step',
      text: 'Rebasing onto the default branch',
      clock: 'running',
    });
  });

  it('counts the wait of a waiting step', () => {
    // Act
    const status = rowStatus(
      step({ step: 'merge', actor: 'code', status: 'current', phase: 'waiting' }),
      'live',
    );

    // Assert
    expect(status).toEqual({ kind: 'step', text: 'Merge', clock: 'waiting' });
  });

  it('sentence-cases a step it has no copy for', () => {
    // Act
    const status = rowStatus(
      step({ step: 'mark_ready', actor: 'code', status: 'current', phase: 'running' }),
      'live',
    );

    // Assert
    expect(status).toEqual({ kind: 'step', text: 'Mark ready', clock: 'running' });
  });
});

describe('isRunningRow', () => {
  it.each([
    ['running', true],
    ['preparing', true],
    ['retrying', true],
    ['waiting', false],
  ] as const)('loops a row whose current step is %s: %s', (phase, running) => {
    expect(
      isRunningRow(step({ step: 'checks', actor: 'code', status: 'current', phase }), 'live'),
    ).toBe(running);
  });

  it('never loops a Needs you row', () => {
    expect(isRunningRow(needs, 'live')).toBe(false);
  });

  it('never loops a completed row', () => {
    expect(isRunningRow(worker(null), 'confirm')).toBe(false);
  });
});

describe('currentStepLabel', () => {
  it('is the current step label', () => {
    expect(
      currentStepLabel([
        { step: 'your_review', actor: 'person', status: 'current', phase: 'needs_you' },
      ]),
    ).toBe('Your review');
  });
});
