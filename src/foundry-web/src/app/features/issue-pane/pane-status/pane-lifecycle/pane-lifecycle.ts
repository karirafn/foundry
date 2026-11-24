import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { AgentActivity } from '../../../../core/models/agent-activity.model';
import { WorkerSignalRService } from '../../../../core/services/worker-signalr.service';
import { LifecycleRailComponent } from '../../../../shared/components/lifecycle-rail/lifecycle-rail';
import { TaskListComponent } from '../../../../shared/components/task-list/task-list';
import {
  TaskListItem,
  taskListItems,
  taskOrdinal,
} from '../../../../shared/utils/agent-activity-format';
import { LifecycleStep, currentStep } from '../../../issues/lifecycle.model';

let nextId = 0;

const TICK_MS = 1000;

/**
 * The pane's Lifecycle section: the vertical rail, with the running Claude step's task checklist
 * nested under that step behind a Tasks toggle, open by default.
 */
@Component({
  selector: 'fd-pane-lifecycle',
  imports: [LifecycleRailComponent, TaskListComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pane-lifecycle.html',
  styleUrl: './pane-lifecycle.scss',
})
export class PaneLifecycleComponent {
  readonly issueId: InputSignal<string> = input.required<string>();
  readonly lifecycle: InputSignal<readonly LifecycleStep[]> =
    input.required<readonly LifecycleStep[]>();
  readonly round: InputSignal<number> = input.required<number>();
  /** When the issue entered its current step (ISO). */
  readonly enteredAt: InputSignal<string | null> = input<string | null>(null);
  /** Emits when the task toggle or list is removed while it holds focus. */
  readonly focusLost: OutputEmitterRef<void> = output<void>();

  protected readonly headingId = `pane-lifecycle-heading-${nextId}`;
  protected readonly listId = `pane-lifecycle-tasks-${nextId++}`;

  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _host: HTMLElement = inject(ElementRef).nativeElement;
  private readonly _document = inject(DOCUMENT);
  private readonly _injector = inject(Injector);
  private readonly _taskList: Signal<TaskListComponent | undefined> = viewChild(TaskListComponent);

  private readonly _now: WritableSignal<number> = signal(Date.now());
  protected readonly now: Signal<number> = this._now.asReadonly();

  private readonly _current: Signal<LifecycleStep | null> = computed(() =>
    currentStep(this.lifecycle()),
  );
  private readonly _hasCurrent: Signal<boolean> = computed(() => this._current() !== null);

  // The agent's activity is dropped when its run ends, so only a running Claude step has tasks.
  private readonly _activity: Signal<AgentActivity | null> = computed(() => {
    const current = this._current();
    return current?.actor === 'claude' && current.phase === 'running'
      ? this._workerSignalR.agentActivityForIssue(this.issueId())
      : null;
  });

  protected readonly tasks: Signal<readonly TaskListItem[]> = computed(() => {
    const activity = this._activity();
    return activity === null ? [] : taskListItems(activity);
  });

  protected readonly progress: Signal<string> = computed(() => {
    const activity = this._activity();
    return activity === null ? '' : `task ${taskOrdinal(activity)} of ${activity.tasks.length}`;
  });

  protected readonly hasTasks: Signal<boolean> = computed(() => this.tasks().length > 0);

  // Open by default, and open again for each issue.
  protected readonly expanded: WritableSignal<boolean> = linkedSignal({
    source: this.issueId,
    computation: () => true,
  });

  constructor() {
    effect((onCleanup) => {
      if (!this._hasCurrent()) {
        return;
      }
      const timer = setInterval(() => this._now.set(Date.now()), TICK_MS);
      onCleanup(() => clearInterval(timer));
    });

    // Runs before the template drops the toggle, so it can still see whether focus is inside it.
    effect(() => {
      if (this.hasTasks()) {
        return;
      }
      untracked(() => {
        const extra = this._host.querySelector('.rail__extra');
        if (extra !== null && extra.contains(this._document.activeElement)) {
          this.focusLost.emit();
        }
      });
    });
  }

  protected toggle(): void {
    this.expanded.update((open) => !open);
    if (this.expanded()) {
      afterNextRender(() => this._taskList()?.revealCurrent(), { injector: this._injector });
    }
  }
}
