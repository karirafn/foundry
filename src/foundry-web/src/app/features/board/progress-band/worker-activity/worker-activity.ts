import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { ActivityPanel } from './worker-activity.model';

/**
 * A worker card's live readout: the current task, a segmented task bar with its position, and the
 * latest tool. It has no live region; the board announces silence once, never task ticks.
 */
@Component({
  selector: 'fd-worker-activity',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './worker-activity.html',
  styleUrl: './worker-activity.scss',
})
export class WorkerActivityComponent {
  readonly panel: InputSignal<ActivityPanel> = input.required<ActivityPanel>();
  /** The task line's id, so the card's button can be described by it. */
  readonly taskLineId: InputSignal<string> = input.required<string>();
}
