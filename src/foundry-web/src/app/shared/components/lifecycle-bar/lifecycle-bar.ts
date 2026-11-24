import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  input,
  linkedSignal,
} from '@angular/core';
import { LifecycleStep } from '../../../features/issues/lifecycle.model';
import { BarSegmentView, barView } from './lifecycle-bar.model';

/**
 * An issue's lifecycle as a progress bar: one segment per rail node, the current one taller and in
 * its tone. Hidden from assistive tech, because the row's marker already names the step.
 */
@Component({
  selector: 'fd-lifecycle-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  templateUrl: './lifecycle-bar.html',
  styleUrl: './lifecycle-bar.scss',
})
export class LifecycleBarComponent {
  readonly lifecycle: InputSignal<readonly LifecycleStep[]> =
    input.required<readonly LifecycleStep[]>();
  /** The issue just arrived in its band. */
  readonly arriving: InputSignal<boolean> = input<boolean>(false);

  // Diffs against the lifecycle shown before, so a change plays once and the first render never moves.
  protected readonly segments: Signal<readonly BarSegmentView[]> = linkedSignal({
    source: this.lifecycle,
    computation: (lifecycle, previous) => barView(lifecycle, previous?.source ?? null),
  });
}
