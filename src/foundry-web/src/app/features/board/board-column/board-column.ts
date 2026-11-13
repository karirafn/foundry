import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  input,
  output,
} from '@angular/core';
import { BoardCardComponent } from '../board-card/board-card';
import { BoardColumnView } from '../board-state.service';

export interface CardOpened {
  readonly id: string;
  readonly invoker: HTMLElement;
}

const SKELETONS = [0, 1];

/** One stage: its header, then the queue, in-flow and needs-you cells, each on its own grid row. */
@Component({
  selector: 'fd-board-column',
  imports: [BoardCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'region',
    '[attr.aria-labelledby]': 'headingId()',
    '[attr.aria-busy]': 'loading() || null',
    '[style.grid-column]': 'position()',
  },
  templateUrl: './board-column.html',
  styleUrl: './board-column.scss',
})
export class BoardColumnComponent {
  readonly column: InputSignal<BoardColumnView> = input.required<BoardColumnView>();
  /** 1-based position of the column in the grid. */
  readonly position: InputSignal<number> = input.required<number>();
  readonly loading: InputSignal<boolean> = input<boolean>(false);
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  protected readonly skeletons = SKELETONS;
  protected readonly headingId: Signal<string> = computed(
    () => `board-stage-${this.column().stage.id}`,
  );
  protected readonly label: Signal<string> = computed(() => this.column().stage.label);
  protected readonly countText: Signal<string> = computed(() => {
    const count = this.column().count;
    return count === 1 ? ', 1 issue' : `, ${count} issues`;
  });
}
