import {
  LifecycleActor,
  LifecyclePhase,
  LifecycleStatus,
  LifecycleStep,
} from '../../../features/issues/lifecycle.model';
import { sentenceCase, stepCopy } from './lifecycle-steps';

/** How a node is drawn: its colour, from the actor, or from a failure or the finished issue. */
export type NodeTone = LifecycleActor | 'failed' | 'done';
export type NodeGlyph = 'claude' | 'code' | 'person' | 'failed' | 'check';
/**
 * The ring around a current node: a sweeping arc, a turning dashed ring, or a still one; `queued`
 * is the still dashed ring of a node whose queue wait is current.
 */
export type NodeRing = 'arc' | 'dashed' | 'solid' | 'queued' | null;
/** A change since the previous lifecycle, played once. */
export type NodeMotion = 'pop' | 'settle' | 'shake' | null;
export type ConnectorFill = 'none' | 'half' | 'full';

/** A node of the lifecycle once its queue waits are folded away. */
export interface FoldedNode {
  readonly step: LifecycleStep;
  /** The queue wait before this node is current: the issue waits to start it. */
  readonly queued: boolean;
  readonly motion: NodeMotion;
  /** The connector into the node; null on the first node. */
  readonly lead: ConnectorFill | null;
  /** The lead just changed, so its fill grows once. */
  readonly leadGrow: boolean;
}

export interface RailStepView {
  readonly step: string;
  readonly label: string;
  readonly actor: LifecycleActor;
  /** Who runs the step, as the vertical rail shows it: "Claude", "Foundry code", "You". */
  readonly actorWord: string;
  readonly status: LifecycleStatus;
  readonly phase: LifecyclePhase | null;
  readonly queued: boolean;
  /** The current step, or the node a current queue wait folded into. */
  readonly current: boolean;
  readonly tone: NodeTone;
  readonly glyph: NodeGlyph;
  readonly ring: NodeRing;
  /** The word under the label: "queued", "off", "skipped", or the current step's phase. */
  readonly word: string | null;
  /** The revision round on the Implement node, when past the first. */
  readonly badge: number | null;
  readonly motion: NodeMotion;
  readonly ariaLabel: string;
  /** The horizontal rail's connector into this node; null on the first. */
  readonly lead: ConnectorFill | null;
  readonly leadGrow: boolean;
  /** The vertical rail's line down to the next node; null on the last. */
  readonly line: ConnectorFill | null;
  readonly lineGrow: boolean;
}

const ACTOR_WORDS: Readonly<Record<LifecycleActor, string>> = {
  claude: 'Claude',
  code: 'Foundry code',
  person: 'you',
};

const ACTOR_NAMES: Readonly<Record<LifecycleActor, string>> = {
  claude: 'Claude',
  code: 'Foundry code',
  person: 'You',
};

const STATUS_TEXT: Readonly<Record<Exclude<LifecycleStatus, 'current'>, string>> = {
  done: 'Done',
  upcoming: 'Upcoming',
  off: 'Off for this repository',
  skipped: 'Skipped',
};

const STATUS_WORDS: Readonly<Record<LifecycleStatus, string>> = {
  done: 'done',
  current: 'current',
  upcoming: 'upcoming',
  off: 'off for this repository',
  skipped: 'skipped',
};

export const PHASE_WORDS: Readonly<Record<LifecyclePhase, string>> = {
  waiting: 'waiting',
  preparing: 'preparing',
  running: 'running',
  needs_you: 'needs you',
  failed: 'failed',
  retrying: 'retrying',
};

const MOTION_RANK: Readonly<Record<Exclude<NodeMotion, null>, number>> = {
  settle: 1,
  pop: 2,
  shake: 3,
};

/** A step's colour: its actor's, or the current phase's when that phase is not the actor's own. */
export function tone(step: LifecycleStep): NodeTone {
  if (step.step === 'done' && (step.status === 'done' || step.status === 'current')) {
    return 'done';
  }
  if (step.status !== 'current') {
    return step.actor;
  }
  switch (step.phase) {
    case 'failed':
      return 'failed';
    case 'preparing':
    case 'retrying':
      return 'code';
    case 'needs_you':
      return 'person';
    default:
      return step.actor;
  }
}

function glyph(step: LifecycleStep, nodeTone: NodeTone): NodeGlyph {
  if (nodeTone === 'done') {
    return 'check';
  }
  return nodeTone === 'failed' ? 'failed' : nodeTone;
}

function ring(step: LifecycleStep): NodeRing {
  if (step.status !== 'current' || step.step === 'done') {
    return null;
  }
  switch (step.phase) {
    case 'running':
      return step.actor === 'claude' ? 'arc' : 'dashed';
    case 'preparing':
    case 'retrying':
      return 'dashed';
    case 'needs_you':
    case 'failed':
      return 'solid';
    default:
      return null;
  }
}

function word(step: LifecycleStep): string | null {
  if (step.status === 'off') {
    return 'off';
  }
  if (step.status === 'skipped') {
    return 'skipped';
  }
  if (step.status === 'current' && step.phase !== null && step.phase !== 'running') {
    return PHASE_WORDS[step.phase];
  }
  return null;
}

function motion(step: LifecycleStep, before: LifecycleStep | undefined): NodeMotion {
  if (before === undefined) {
    return null;
  }
  if (step.status === 'current' && step.phase === 'failed' && before.phase !== 'failed') {
    return 'shake';
  }
  if (step.status === 'current' && before.status !== 'current') {
    return 'pop';
  }
  if (step.status === 'done' && before.status !== 'done') {
    return step.step === 'done' ? 'pop' : 'settle';
  }
  return null;
}

function fillOf(node: LifecycleStep, queued: boolean): ConnectorFill {
  if (queued) {
    return 'half';
  }
  return node.status === 'done' || node.status === 'current' ? 'full' : 'none';
}

function larger(a: NodeMotion, b: NodeMotion): NodeMotion {
  if (a === null) {
    return b;
  }
  return b !== null && MOTION_RANK[b] > MOTION_RANK[a] ? b : a;
}

interface Fold {
  readonly step: LifecycleStep;
  /** The current queue wait folded into this node, if any. */
  readonly wait: LifecycleStep | null;
}

function fold(lifecycle: readonly LifecycleStep[]): readonly Fold[] {
  const nodes: Fold[] = [];
  let wait: LifecycleStep | null = null;
  for (const step of lifecycle) {
    if (stepCopy(step.step).kind === 'wait') {
      wait = step.status === 'current' ? step : wait;
      continue;
    }
    nodes.push({ step, wait });
    wait = null;
  }
  return nodes;
}

/**
 * The lifecycle's nodes in served order, with its queue waits folded away: a current wait marks
 * the node after it as queued, and a done or upcoming wait is dropped. `previous` is the lifecycle
 * shown before; a node that changed since then gets a one-shot motion, and its lead grows once.
 * With no previous lifecycle (the first render) nothing moves.
 */
export function foldWaits(
  lifecycle: readonly LifecycleStep[],
  previous: readonly LifecycleStep[] | null,
): readonly FoldedNode[] {
  const before = new Map((previous ?? []).map((step) => [step.step, step]));
  const beforeNodes = new Map(fold(previous ?? []).map((node) => [node.step.step, node]));
  return fold(lifecycle).map(({ step, wait }, i) => {
    const queued = wait !== null;
    const lead = i === 0 ? null : fillOf(step, queued);
    const prior = beforeNodes.get(step.step);
    const leadGrow =
      prior !== undefined &&
      lead !== null &&
      lead !== 'none' &&
      fillOf(prior.step, prior.wait !== null) !== lead;
    const nodeMotion =
      previous === null
        ? null
        : larger(
            motion(step, before.get(step.step)),
            wait === null ? null : motion(wait, before.get(wait.step)),
          );
    return { step, queued, motion: nodeMotion, lead, leadGrow };
  });
}

/**
 * Lays an issue's served lifecycle out as a rail: one node per step, with each current queue wait
 * shown on the node after it. `previous` is the lifecycle the rail showed before (see `foldWaits`).
 */
export function railView(
  lifecycle: readonly LifecycleStep[],
  round: number,
  previous: readonly LifecycleStep[] | null,
): readonly RailStepView[] {
  const nodes = foldWaits(lifecycle, previous);
  return nodes.map(({ step, queued, motion: nodeMotion, lead, leadGrow }, i) => {
    const label = stepCopy(step.step).label;
    const nodeTone = tone(step);
    const badge =
      step.step === 'implement' &&
      round > 1 &&
      (step.status === 'done' || step.status === 'current')
        ? round
        : null;
    const phase =
      step.status === 'current' && step.phase !== null ? `, ${PHASE_WORDS[step.phase]}` : '';
    const next = nodes[i + 1] as FoldedNode | undefined;
    return {
      step: step.step,
      label,
      actor: step.actor,
      actorWord: ACTOR_NAMES[step.actor],
      status: step.status,
      phase: step.phase,
      queued,
      current: queued || step.status === 'current',
      tone: nodeTone,
      glyph: glyph(step, nodeTone),
      ring: queued ? 'queued' : ring(step),
      word: queued ? 'queued' : word(step),
      badge,
      motion: nodeMotion,
      lead,
      leadGrow,
      line: next?.lead ?? null,
      lineGrow: next?.leadGrow ?? false,
      ariaLabel: `${label}${badge === null ? '' : `, ×${badge}`}, by ${ACTOR_WORDS[step.actor]}, ${STATUS_WORDS[step.status]}${phase}${queued ? ', queued' : ''}`,
    };
  });
}

/**
 * A step's status as the vertical rail words it: "Done", "Upcoming", or for the current step its
 * phase and `elapsed` ("Running · 12m"); a queued node reads "Queued", with its wait's elapsed time.
 * A null `elapsed` (start unknown) leaves the time out.
 */
export function stepStatusText(step: RailStepView, elapsed: string | null): string {
  if (step.status !== 'current' && !step.queued) {
    return STATUS_TEXT[step.status];
  }
  const phase = step.queued
    ? 'Queued'
    : step.phase === null
      ? 'In progress'
      : sentenceCase(PHASE_WORDS[step.phase]);
  return elapsed === null ? phase : `${phase} · ${elapsed}`;
}
