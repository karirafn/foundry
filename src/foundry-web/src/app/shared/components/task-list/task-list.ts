import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  InputSignal,
  Signal,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { TaskListItem } from '../../utils/agent-activity-format';
import { TaskListRow, sameTaskItems, taskListRows } from './task-list.model';

const PREFIXES: Readonly<Record<TaskListItem['state'], string>> = {
  done: 'Done: ',
  current: 'In progress: ',
  upcoming: 'Pending: ',
};

/**
 * The agent's checklist: done, current and upcoming tasks, and the subagent running the current one.
 * A task that completes ticks once and the next one slides in once; the first render never moves.
 * It has no live region: task ticks are never announced.
 */
@Component({
  selector: 'fd-task-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './task-list.html',
  styleUrl: './task-list.scss',
})
export class TaskListComponent {
  readonly items: InputSignal<readonly TaskListItem[]> = input.required<readonly TaskListItem[]>();
  readonly listId: InputSignal<string> = input.required<string>();

  protected readonly prefixes = PREFIXES;

  private readonly _host: HTMLElement = inject(ElementRef).nativeElement;

  // Activity arrives on every agent event; only a change to the list itself counts as a change.
  private readonly _items: Signal<readonly TaskListItem[]> = computed(() => this.items(), {
    equal: sameTaskItems,
  });

  protected readonly rows: Signal<readonly TaskListRow[]> = linkedSignal({
    source: this._items,
    computation: (items, previous) => taskListRows(items, previous?.source ?? null),
  });

  /**
   * Scrolls the list, and only the list, so the current task is in view. `scrollIntoView` would also
   * scroll the page to the row.
   */
  revealCurrent(): void {
    const list = this._host.querySelector<HTMLElement>('.task-list');
    const current = list?.querySelector<HTMLElement>('[data-state="current"]');
    if (!list || !current) {
      return;
    }
    const top = current.offsetTop;
    const bottom = top + current.offsetHeight;
    if (top < list.scrollTop) {
      list.scrollTop = top;
    } else if (bottom > list.scrollTop + list.clientHeight) {
      list.scrollTop = bottom - list.clientHeight;
    }
  }
}
