import {
  LifecycleActor,
  LifecyclePhase,
  LifecycleStatus,
  LifecycleStep,
} from '../../../features/issues/lifecycle.model';
import { RAIL_STAGE_LABELS, RailStage, sentenceCase, stepCopy } from './lifecycle-steps';

/** How a node is drawn: its colour, from the actor, or from a failure or the finished issue. */
export type NodeTone = LifecycleActor | 'failed' | 'done';
export type NodeGlyph = 'claude' | 'code' | 'person' | 'failed' | 'check';
/** The ring around a current node: a sweeping arc, a turning dashed ring, or a still one. */
export type NodeRing = 'arc' | 'dashed' | 'solid' | null;
/** A change since the previous lifecycle, played once. */
export type NodeMotion = 'pop' | 'settle' | 'shake' | null;
export type ConnectorFill = 'none' | 'half' | 'full';

export interface RailStepView {
  readonly step: string;
  readonly label: string;
  readonly kind: 'node' | 'wait';
  readonly stage: RailStage;
  readonly actor: LifecycleActor;
  /** Who runs the step, as the vertical rail shows it: "Claude", "Foundry code", "You". */
  readonly actorWord: string;
  readonly status: LifecycleStatus;
  readonly phase: LifecyclePhase | null;
  /** The step's grid column, 1-based. */
  readonly column: number;
  readonly tone: NodeTone;
  readonly glyph: NodeGlyph;
  readonly ring: NodeRing;
  /** The word under the label: "off", "skipped", or the current step's phase. */
  readonly word: string | null;
  /** The revision round on the Implement node, when past the first. */
  readonly badge: number | null;
  readonly motion: NodeMotion;
  readonly ariaLabel: string;
  /** The vertical rail's line down to the next step in the same stage; null after a stage's last step. */
  readonly line: ConnectorFill | null;
  readonly lineGrow: boolean;
}

export interface RailConnectorView {
  /** First grid column the line covers (the node before it, hidden behind that node). */
  readonly from: number;
  /** The grid column after the line's last. */
  readonly to: number;
  readonly fill: ConnectorFill;
  readonly grow: boolean;
}

export interface RailBracketView {
  readonly stage: RailStage;
  readonly label: string;
  readonly from: number;
  readonly to: number;
  /** Every step in the stage is off or skipped. */
  readonly dim: boolean;
  readonly current: boolean;
}

/** A stage of the vertical rail: its label and its steps, in order. */
export interface RailGroupView {
  readonly stage: RailStage;
  readonly label: string;
  /** Every step in the stage is off or skipped. */
  readonly dim: boolean;
  readonly current: boolean;
  readonly steps: readonly RailStepView[];
}

export interface RailView {
  /** The `grid-template-columns` value: `auto` per node, a stretching track per connector. */
  readonly columns: string;
  readonly steps: readonly RailStepView[];
  readonly connectors: readonly RailConnectorView[];
  readonly brackets: readonly RailBracketView[];
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

const CONNECTOR_TRACK = 'minmax(var(--fd-rail-gap), 1fr)';

function tone(step: LifecycleStep): NodeTone {
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

function fillOf(target: LifecycleStep | undefined, wait: boolean): ConnectorFill {
  if (target === undefined) {
    return 'none';
  }
  if (target.status === 'done') {
    return 'full';
  }
  if (target.status === 'current') {
    return wait ? 'half' : 'full';
  }
  return 'none';
}

interface Track {
  readonly kind: 'node' | 'connector';
  /** The step in this track: a node, or the wait drawn on a connector. */
  step: number | null;
}

/**
 * Lays an issue's served lifecycle out as a rail: one grid column per node, one stretching column
 * between nodes for the connector (holding a queue wait's dot when there is one), and a bracket
 * per stage. `previous` is the lifecycle the rail showed before; steps that changed since then get
 * a one-shot motion, and with no previous lifecycle (the first render) nothing moves.
 */
export function railView(
  lifecycle: readonly LifecycleStep[],
  round: number,
  previous: readonly LifecycleStep[] | null,
): RailView {
  const tracks: Track[] = [];
  const columnOf: number[] = [];
  let stage: RailStage = 'triage';
  const copies = lifecycle.map((step) => {
    const copy = stepCopy(step.step, stage);
    stage = copy.stage;
    return copy;
  });

  copies.forEach((copy, i) => {
    const last = tracks.at(-1);
    if (copy.kind === 'wait') {
      tracks.push({ kind: 'connector', step: i });
    } else {
      if (last?.kind === 'node') {
        tracks.push({ kind: 'connector', step: null });
      }
      tracks.push({ kind: 'node', step: i });
    }
    columnOf[i] = tracks.length;
  });

  const before = new Map((previous ?? []).map((step) => [step.step, step]));
  const steps: RailStepView[] = lifecycle.map((step, i) => {
    const copy = copies[i];
    const nodeTone = tone(step);
    const badge =
      step.step === 'implement' &&
      round > 1 &&
      (step.status === 'done' || step.status === 'current')
        ? round
        : null;
    const phase =
      step.status === 'current' && step.phase !== null ? `, ${PHASE_WORDS[step.phase]}` : '';
    const next = lifecycle[i + 1] as LifecycleStep | undefined;
    const line =
      next === undefined || copies[i + 1].stage !== copy.stage ? null : fillOf(next, false);
    const prior = previous === null || next === undefined ? undefined : before.get(next.step);
    return {
      step: step.step,
      label: copy.label,
      kind: copy.kind,
      stage: copy.stage,
      actor: step.actor,
      actorWord: ACTOR_NAMES[step.actor],
      status: step.status,
      phase: step.phase,
      column: columnOf[i],
      tone: nodeTone,
      glyph: glyph(step, nodeTone),
      ring: ring(step),
      word: word(step),
      badge,
      motion: previous === null ? null : motion(step, before.get(step.step)),
      line,
      lineGrow: prior !== undefined && line === 'full' && fillOf(prior, false) !== 'full',
      ariaLabel: `${copy.label}${badge === null ? '' : `, ×${badge}`}, by ${ACTOR_WORDS[step.actor]}, ${STATUS_WORDS[step.status]}${phase}`,
    };
  });

  const connectors: RailConnectorView[] = [];
  tracks.forEach((track, t) => {
    if (track.kind !== 'connector') {
      return;
    }
    const column = t + 1;
    const wait = track.step !== null;
    const targetIndex = wait ? track.step! : (tracks[t + 1]?.step ?? null);
    const target = targetIndex === null ? undefined : lifecycle[targetIndex];
    const fill = fillOf(target, wait);
    const prior = previous === null || target === undefined ? undefined : before.get(target.step);
    const grow = prior !== undefined && fill !== 'none' && fillOf(prior, wait) !== fill;
    const from = tracks[t - 1]?.kind === 'node' ? column - 1 : column;
    connectors.push({ from, to: column + 1, fill, grow });
  });

  const brackets: RailBracketView[] = [];
  steps.forEach((step) => {
    const last = brackets.at(-1);
    const inactive = step.status === 'off' || step.status === 'skipped';
    if (last !== undefined && last.stage === step.stage) {
      brackets[brackets.length - 1] = {
        ...last,
        to: step.column + 1,
        dim: last.dim && inactive,
        current: last.current || step.status === 'current',
      };
      return;
    }
    brackets.push({
      stage: step.stage,
      label: RAIL_STAGE_LABELS[step.stage],
      from: step.column,
      to: step.column + 1,
      dim: inactive,
      current: step.status === 'current',
    });
  });

  return {
    columns: tracks.map((t) => (t.kind === 'node' ? 'auto' : CONNECTOR_TRACK)).join(' '),
    steps,
    connectors,
    brackets,
  };
}

/** The vertical rail's stages, each holding its steps. */
export function railGroups(view: RailView): readonly RailGroupView[] {
  return view.brackets.map((bracket) => ({
    stage: bracket.stage,
    label: bracket.label,
    dim: bracket.dim,
    current: bracket.current,
    steps: view.steps.filter((s) => s.column >= bracket.from && s.column < bracket.to),
  }));
}

/**
 * A step's status as the vertical rail words it: "Done", "Upcoming", or for the current step its
 * phase and `elapsed` ("Running · 12m"). A null `elapsed` (start unknown) leaves the time out.
 */
export function stepStatusText(step: RailStepView, elapsed: string | null): string {
  if (step.status !== 'current') {
    return STATUS_TEXT[step.status];
  }
  const phase = step.phase === null ? 'In progress' : sentenceCase(PHASE_WORDS[step.phase]);
  return elapsed === null ? phase : `${phase} · ${elapsed}`;
}
