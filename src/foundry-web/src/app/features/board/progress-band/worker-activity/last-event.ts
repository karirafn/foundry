import { ChangeDetectionStrategy, Component, InputSignal, input } from '@angular/core';
import { LastEventLine } from './worker-activity.model';

/**
 * A running worker's last-event line: when it last reported, its silence, or the background tasks it
 * waits on. Not a live region: it ticks every second; the board announces silence once.
 */
@Component({
  selector: 'fd-last-event',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './last-event.html',
  styleUrl: './last-event.scss',
})
export class LastEventComponent {
  readonly line: InputSignal<LastEventLine> = input.required<LastEventLine>();
}
