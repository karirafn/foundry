import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  computed,
  input,
  linkedSignal,
} from '@angular/core';
import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { NodeTone, RailView, railView } from './lifecycle-rail.model';

export type RailOrientation = 'horizontal' | 'vertical';
/** `full` labels every step under stage brackets; `mini` is dots only, static and hidden from assistive tech. */
export type RailDensity = 'full' | 'mini';

// Set inline rather than per tone in the stylesheet, which keeps it inside its style budget.
const TONES: Readonly<Record<NodeTone, { readonly tone: string; readonly tint: string }>> = {
  claude: { tone: 'var(--fd-accent-working)', tint: 'var(--fd-bg-working-subtle)' },
  code: { tone: 'var(--fd-text-link)', tint: 'var(--fd-bg-code-subtle)' },
  person: { tone: 'var(--fd-text-warning)', tint: 'var(--fd-bg-warning-subtle)' },
  failed: { tone: 'var(--fd-text-error)', tint: 'var(--fd-bg-error-subtle)' },
  done: { tone: 'var(--fd-bg-page)', tint: 'var(--fd-accent-ready)' },
};

/**
 * An issue's whole lifecycle path as the server serves it: every step in order, who runs it, its
 * status, and the current step highlighted. It renders the served path and never works it out.
 */
@Component({
  selector: 'fd-lifecycle-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './lifecycle-rail.html',
  styleUrl: './lifecycle-rail.scss',
})
export class LifecycleRailComponent {
  readonly lifecycle: InputSignal<readonly LifecycleStep[]> =
    input.required<readonly LifecycleStep[]>();
  readonly round: InputSignal<number> = input<number>(1);
  readonly orientation: InputSignal<RailOrientation> = input<RailOrientation>('horizontal');
  readonly density: InputSignal<RailDensity> = input<RailDensity>('full');
  /** The issue just started needing a person: its needs-you node beacons. */
  readonly arriving: InputSignal<boolean> = input<boolean>(false);

  protected readonly tones = TONES;

  protected readonly mini: Signal<boolean> = computed(() => this.density() === 'mini');

  // Diffs against the lifecycle shown before, so a change plays once and the first render never moves.
  protected readonly view: Signal<RailView> = linkedSignal({
    source: () => ({ lifecycle: this.lifecycle(), round: this.round(), mini: this.mini() }),
    computation: (source, previous) =>
      railView(
        source.lifecycle,
        source.round,
        source.mini || previous === undefined ? null : previous.source.lifecycle,
      ),
  });
}
