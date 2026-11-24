import {
  LifecyclePhase,
  LifecycleStatus,
  LifecycleStep,
} from '../../../features/issues/lifecycle.model';
import { NodeMotion, TONES, foldWaits, tone } from '../lifecycle-rail/lifecycle-rail.model';

/** A change since the previous lifecycle, played once: a step done fills, a new current step settles. */
export type SegmentMotion = 'fill' | 'settle' | 'shake' | null;

export interface BarSegmentView {
  readonly step: string;
  readonly status: LifecycleStatus;
  readonly phase: LifecyclePhase | null;
  /** The queue wait before this step is current: the issue waits to start it. */
  readonly queued: boolean;
  /** The colour of the current or queued segment: its actor's, or the current phase's. */
  readonly tone: string;
  readonly motion: SegmentMotion;
}

function segmentMotion(motion: NodeMotion, status: LifecycleStatus): SegmentMotion {
  if (motion === null) {
    return null;
  }
  if (motion === 'shake') {
    return 'shake';
  }
  return status === 'done' ? 'fill' : 'settle';
}

// The finished issue's tone is drawn on its tint, so the ghosted Done segment takes the tint.
function colour(step: LifecycleStep): string {
  const nodeTone = tone(step);
  return nodeTone === 'done' ? TONES.done.tint : TONES[nodeTone].tone;
}

/**
 * Lays an issue's served lifecycle out as a bar: one segment per rail node, with a current queue
 * wait shown on the segment after it. `previous` is the lifecycle shown before; a segment that
 * changed since then moves once, and with no previous lifecycle (the first render) nothing moves.
 */
export function barView(
  lifecycle: readonly LifecycleStep[],
  previous: readonly LifecycleStep[] | null,
): readonly BarSegmentView[] {
  return foldWaits(lifecycle, previous).map(({ step, queued, motion }) => ({
    step: step.step,
    status: step.status,
    phase: step.phase,
    queued,
    tone: colour(step),
    motion: segmentMotion(motion, step.status),
  }));
}
