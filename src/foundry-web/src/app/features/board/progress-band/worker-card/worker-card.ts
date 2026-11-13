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
import { RouterLink } from '@angular/router';
import { RoleTagComponent } from '../../../../shared/components/role-tag/role-tag';
import { formatElapsed } from '../../../../shared/utils/agent-activity-format';
import { ItemView } from '../../board-state.service';
import { WorkerItem } from '../../board.model';

/** A running worker: its role, issue and how long it has run, with a way to watch it on the Floor. */
@Component({
  selector: 'fd-worker-card',
  imports: [RoleTagComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-issue-id]': 'item().issue.id',
    '[style.view-transition-name]': "'fd-card-' + item().issue.id",
    '[style.view-transition-class]': "'fd-card'",
  },
  templateUrl: './worker-card.html',
  styleUrl: './worker-card.scss',
})
export class WorkerCardComponent {
  readonly item: InputSignal<ItemView<WorkerItem>> = input.required<ItemView<WorkerItem>>();
  /** The current time, in epoch ms, from the page's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  /** Emits the card's button, so focus can return to it when the pane closes. */
  readonly open: OutputEmitterRef<HTMLElement> = output<HTMLElement>();

  protected readonly idBase: Signal<string> = computed(() => `worker-card-${this.item().issue.id}`);
  protected readonly label: Signal<string> = computed(
    () => `${this.item().issue.title}, #${this.item().issue.issueNumber}`,
  );
  /** The Floor lane heading's id, which the Floor scrolls to and focuses. */
  protected readonly laneFragment: Signal<string> = computed(
    () => `floor-lane-${this.item().issue.id}`,
  );
  protected readonly elapsed: Signal<string> = computed(() =>
    formatElapsed(this.now() - Date.parse(this.item().issue.enteredStateAt)),
  );
}
