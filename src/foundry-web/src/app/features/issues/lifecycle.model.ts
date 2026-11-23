/** Who runs a lifecycle step: a Claude worker, Foundry's own code, or a person. */
export type LifecycleActor = 'claude' | 'code' | 'person';

export type LifecycleStatus = 'done' | 'current' | 'upcoming' | 'off' | 'skipped';

export type LifecyclePhase =
  'waiting' | 'preparing' | 'running' | 'needs_you' | 'failed' | 'retrying';

/**
 * One step on an issue's lifecycle path, as the server serves it. `step` stays a string, so a step
 * this dashboard has no copy for yet is still shown; `phase` is set only on the current step.
 */
export interface LifecycleStep {
  readonly step: string;
  readonly actor: LifecycleActor;
  readonly status: LifecycleStatus;
  readonly phase: LifecyclePhase | null;
}

interface WireLifecycleStep {
  readonly step: string;
  readonly actor: string;
  readonly status: string;
  readonly phase?: string | null;
}

const ACTORS: ReadonlySet<string> = new Set<LifecycleActor>(['claude', 'code', 'person']);
const STATUSES: ReadonlySet<string> = new Set<LifecycleStatus>([
  'done',
  'current',
  'upcoming',
  'off',
  'skipped',
]);
const PHASES: ReadonlySet<string> = new Set<LifecyclePhase>([
  'waiting',
  'preparing',
  'running',
  'needs_you',
  'failed',
  'retrying',
]);

/**
 * Narrows a served step. An unknown actor reads as Foundry code, an unknown status as upcoming and
 * an unknown phase as none, so a value a newer server sends still renders as a plain node.
 */
export function toLifecycleStep(wire: WireLifecycleStep): LifecycleStep {
  return {
    step: wire.step,
    actor: ACTORS.has(wire.actor) ? (wire.actor as LifecycleActor) : 'code',
    status: STATUSES.has(wire.status) ? (wire.status as LifecycleStatus) : 'upcoming',
    phase: wire.phase != null && PHASES.has(wire.phase) ? (wire.phase as LifecyclePhase) : null,
  };
}

/** The step the issue is on, or null when the path has none. */
export function currentStep(lifecycle: readonly LifecycleStep[] | undefined): LifecycleStep | null {
  return lifecycle?.find((s) => s.status === 'current') ?? null;
}
