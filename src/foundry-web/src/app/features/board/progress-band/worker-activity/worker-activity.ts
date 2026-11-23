import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  InputSignal,
  OutputEmitterRef,
  Signal,
  afterNextRender,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  viewChild,
} from '@angular/core';
import { TaskListComponent } from '../../../../shared/components/task-list/task-list';
import { ActivityPanel, TaskSegment } from './worker-activity.model';

/** A segment as rendered: `fill` plays the segment's fill once, as its task completes. */
interface SegmentView {
  readonly state: TaskSegment;
  readonly fill: boolean;
}

function sameSegments(a: readonly TaskSegment[], b: readonly TaskSegment[]): boolean {
  return a.length === b.length && a.every((segment, i) => segment === b[i]);
}

/**
 * A running agent's live readout: the current task, a task bar (segments, or a meter above 20 tasks)
 * with its position, and a Tasks toggle over the agent's checklist. It has no live region; the board
 * announces silence once, never task ticks.
 */
@Component({
  selector: 'fd-worker-activity',
  imports: [TaskListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './worker-activity.html',
  styleUrl: './worker-activity.scss',
})
export class WorkerActivityComponent {
  readonly panel: InputSignal<ActivityPanel> = input.required<ActivityPanel>();
  /** The task line's id, so the row's button can be described by it. */
  readonly taskLineId: InputSignal<string> = input.required<string>();
  /** The checklist's id, named by the toggle's `aria-controls`. */
  readonly listId: InputSignal<string> = input.required<string>();
  readonly expanded: InputSignal<boolean> = input<boolean>(false);
  readonly toggled: OutputEmitterRef<void> = output<void>();

  private readonly _injector = inject(Injector);
  private readonly _taskList: Signal<TaskListComponent | undefined> = viewChild(TaskListComponent);

  // Activity arrives on every agent event; only a change to the bar itself counts as a change.
  private readonly _segments: Signal<readonly TaskSegment[]> = computed(
    () => {
      const shape = this.panel().taskBar?.shape;
      return shape?.kind === 'segments' ? shape.segments : [];
    },
    { equal: sameSegments },
  );

  protected readonly segments: Signal<readonly SegmentView[]> = linkedSignal({
    source: this._segments,
    computation: (segments, previous) =>
      segments.map((state, i) => ({
        state,
        fill: previous !== undefined && state === 'done' && previous.source[i] !== 'done',
      })),
  });

  protected toggle(): void {
    const opening = !this.expanded();
    this.toggled.emit();
    if (opening) {
      afterNextRender(() => this._taskList()?.revealCurrent(), { injector: this._injector });
    }
  }
}
