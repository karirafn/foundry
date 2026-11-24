import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { sentenceCase } from '../lifecycle-rail/lifecycle-steps';
import { StepChipTone } from './step-chip';

export interface StepChipView {
  readonly label: string;
  readonly tone: StepChipTone;
}

const RUNNING_LABELS: Readonly<Record<string, string>> = {
  publish: 'Publishing…',
  rebase: 'Rebasing…',
  checks: 'Checks running…',
  merge: 'Merging…',
};

/**
 * The chip for the issue's current lifecycle step: a run starting, or a step Foundry's own code
 * runs. A merge that waits is the catch-up queue's turn (ADR 0111). Claude and person steps,
 * queue waits and failures show no chip; the state badge and callout carry those.
 */
export function stepChipFor(current: LifecycleStep | null): StepChipView | null {
  if (current === null) {
    return null;
  }
  if (current.phase === 'preparing') {
    return { label: 'Preparing…', tone: 'working' };
  }
  if (current.actor !== 'code') {
    return null;
  }
  switch (current.phase) {
    case 'running':
      return { label: runningLabel(current.step), tone: 'working' };
    case 'retrying':
      return { label: `Retrying ${current.step.replaceAll('_', ' ')}…`, tone: 'warning' };
    case 'waiting':
      return current.step === 'merge' ? { label: 'Waiting to catch up', tone: 'waiting' } : null;
    default:
      return null;
  }
}

function runningLabel(step: string): string {
  return Object.hasOwn(RUNNING_LABELS, step) ? RUNNING_LABELS[step] : `${sentenceCase(step)}…`;
}
