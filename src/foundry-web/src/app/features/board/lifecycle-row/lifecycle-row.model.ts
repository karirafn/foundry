import { LifecycleStep, currentStep } from '../../issues/lifecycle.model';
import { stepCopy } from '../../../shared/components/lifecycle-rail/lifecycle-steps';
import { CardPhase } from '../board-state.service';
import { NeedsItem, StepItem, WorkerItem } from '../board.model';

export type LifecycleRowItem = NeedsItem | WorkerItem | StepItem;

/**
 * What a row's status line shows: the completion check, why a person is needed, the live activity
 * of a running Claude step, or one line of step text. `clock` names the time counted beside it.
 */
export type RowStatus =
  | { readonly kind: 'completed' }
  | { readonly kind: 'needs' }
  | { readonly kind: 'activity' }
  | { readonly kind: 'step'; readonly text: string; readonly clock: 'running' | 'waiting' };

/** The current step's label, or null with no lifecycle. */
export function currentStepLabel(lifecycle: readonly LifecycleStep[] | undefined): string | null {
  const current = currentStep(lifecycle);
  return current === null ? null : stepCopy(current.step, 'triage').label;
}

export function rowStatus(item: LifecycleRowItem, phase: CardPhase): RowStatus {
  if (phase !== 'live') {
    return { kind: 'completed' };
  }
  if (item.band === 'needs') {
    return { kind: 'needs' };
  }
  const current = currentStep(item.issue.lifecycle);
  if (current?.phase === 'preparing') {
    return { kind: 'step', text: 'Foundry is preparing the workspace', clock: 'running' };
  }
  if (current?.phase === 'retrying') {
    const label = stepCopy(current.step, 'triage').label.toLowerCase();
    return { kind: 'step', text: `Retrying ${label}`, clock: 'running' };
  }
  if (item.band === 'workers' && (current === null || current.actor === 'claude')) {
    return { kind: 'activity' };
  }
  if (current === null) {
    return { kind: 'step', text: 'In progress', clock: 'running' };
  }
  const copy = stepCopy(current.step, 'triage');
  return current.phase === 'waiting'
    ? { kind: 'step', text: copy.label, clock: 'waiting' }
    : { kind: 'step', text: copy.doing, clock: 'running' };
}

/** The row's current step loops its node: something runs right now. */
export function isRunningRow(item: LifecycleRowItem, phase: CardPhase): boolean {
  if (phase !== 'live' || item.band === 'needs') {
    return false;
  }
  const current = currentStep(item.issue.lifecycle);
  return current === null
    ? item.band === 'workers'
    : current.phase === 'running' || current.phase === 'preparing' || current.phase === 'retrying';
}
