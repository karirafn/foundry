import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  TemplateRef,
  computed,
  input,
  linkedSignal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { LifecycleStep, currentStep } from '../../../features/issues/lifecycle.model';
import { formatElapsed } from '../../utils/agent-activity-format';
import { RailStepView, TONES, ToneColours, railView, stepStatusText } from './lifecycle-rail.model';

export type RailOrientation = 'horizontal' | 'vertical';

function needsYou(lifecycle: readonly LifecycleStep[]): boolean {
  return currentStep(lifecycle)?.phase === 'needs_you';
}

/**
 * An issue's whole lifecycle path as the server serves it: every step in order, who runs it, its
 * status, and the current step highlighted. It renders the served path and never works it out.
 */
@Component({
  selector: 'fd-lifecycle-rail',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-orientation]': 'orientation()' },
  templateUrl: './lifecycle-rail.html',
  styleUrls: ['./lifecycle-rail.scss', './lifecycle-rail-vertical.scss'],
})
export class LifecycleRailComponent {
  readonly lifecycle: InputSignal<readonly LifecycleStep[]> =
    input.required<readonly LifecycleStep[]>();
  readonly round: InputSignal<number> = input<number>(1);
  readonly orientation: InputSignal<RailOrientation> = input<RailOrientation>('horizontal');
  /** The issue just started needing a person: its needs-you node beacons. */
  readonly arriving: InputSignal<boolean> = input<boolean>(false);
  /** When the issue entered its current step (ISO); the vertical rail shows the time since. */
  readonly enteredAt: InputSignal<string | null> = input<string | null>(null);
  /** The current time, in epoch ms, for the elapsed time on the current step. */
  readonly now: InputSignal<number> = input<number>(0);
  /**
   * Rendered inside the vertical rail's current step only. A template, because content projected
   * through `ng-content` inside a `@for` renders once.
   */
  readonly currentStepExtra: InputSignal<TemplateRef<void> | null> =
    input<TemplateRef<void> | null>(null);

  // Diffs against the lifecycle shown before, so a change plays once and the first render never moves.
  protected readonly steps: Signal<readonly RailStepView[]> = linkedSignal({
    source: () => ({ lifecycle: this.lifecycle(), round: this.round() }),
    computation: (source, previous) =>
      railView(
        source.lifecycle,
        source.round,
        previous === undefined ? null : previous.source.lifecycle,
      ),
  });

  protected readonly vertical: Signal<boolean> = computed(() => this.orientation() === 'vertical');

  // The vertical rail has no board to tell it an issue arrived in Needs you, so it beacons itself.
  protected readonly needsYouArrived: Signal<boolean> = linkedSignal({
    source: this.lifecycle,
    computation: (lifecycle, previous) =>
      previous !== undefined && needsYou(lifecycle) && !needsYou(previous.source),
  });

  private readonly _elapsed: Signal<string | null> = computed(() => {
    const enteredAt = this.enteredAt();
    return enteredAt === null ? null : formatElapsed(this.now() - Date.parse(enteredAt));
  });

  // The node template's context is untyped, so the step is typed here. Tones are set inline rather
  // than per tone in the stylesheet, which keeps it inside its style budget.
  protected toneOf(step: RailStepView): ToneColours {
    return TONES[step.tone];
  }

  protected statusText(step: RailStepView): string {
    return stepStatusText(step, this._elapsed());
  }
}
