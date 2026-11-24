import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { stepChipFor } from './step-chip.model';

function current(
  step: string,
  phase: LifecycleStep['phase'],
  actor: LifecycleStep['actor'] = 'code',
): LifecycleStep {
  return { step, actor, status: 'current', phase };
}

describe('stepChipFor', () => {
  it.each([
    ['publish', 'running', 'Publishing…', 'working'],
    ['publish', 'retrying', 'Retrying publish…', 'warning'],
    ['rebase', 'running', 'Rebasing…', 'working'],
    ['checks', 'running', 'Checks running…', 'working'],
    ['merge', 'running', 'Merging…', 'working'],
    ['merge', 'waiting', 'Waiting to catch up', 'waiting'],
  ] as const)('reads %s %s as %s', (step, phase, label, tone) => {
    expect(stepChipFor(current(step, phase))).toEqual({ label, tone });
  });

  it.each(['triage', 'implement', 'review'])('reads a preparing %s step as Preparing…', (step) => {
    expect(stepChipFor(current(step, 'preparing', 'claude'))).toEqual({
      label: 'Preparing…',
      tone: 'working',
    });
  });

  it('reads an unknown running code step in sentence case', () => {
    expect(stepChipFor(current('mark_ready', 'running'))).toEqual({
      label: 'Mark ready…',
      tone: 'working',
    });
  });

  it('shows nothing with no current step', () => {
    expect(stepChipFor(null)).toBeNull();
  });

  it.each([
    ['implement', 'running', 'claude'],
    ['review', 'retrying', 'claude'],
    ['your_review', 'needs_you', 'person'],
    ['publish', 'failed', 'code'],
    ['queued', 'waiting', 'code'],
    ['detected', 'waiting', 'code'],
    ['done', null, 'code'],
  ] as const)('shows nothing for %s %s', (step, phase, actor) => {
    expect(stepChipFor(current(step, phase, actor))).toBeNull();
  });
});
