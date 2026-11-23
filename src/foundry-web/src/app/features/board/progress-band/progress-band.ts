import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  InputSignal,
  OutputEmitterRef,
  Signal,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { DispatchCapacityService } from '../../../core/services/dispatch-capacity.service';
import { IssueService } from '../../issues/issue.service';
import { BoardBandComponent } from '../board-band/board-band';
import { BoardStateService, ItemView } from '../board-state.service';
import { CardOpened, StepItem, WorkerItem } from '../board.model';
import { LifecycleRowComponent } from '../lifecycle-row/lifecycle-row';

// Safety net for occupancy changes no live signal reports (reservations, lowered limits).
const CAPACITY_REFRESH_MS = 30_000;

/**
 * What runs now: a lifecycle row per issue a worker or Foundry's own code is on, and the dispatch
 * slots in use. Stays mounted while empty, so capacity keeps refreshing, but renders nothing then.
 */
@Component({
  selector: 'fd-progress-band',
  imports: [BoardBandComponent, LifecycleRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './progress-band.html',
  styleUrl: './progress-band.scss',
})
export class ProgressBandComponent {
  readonly workers: InputSignal<readonly ItemView<WorkerItem>[]> =
    input.required<readonly ItemView<WorkerItem>[]>();
  readonly steps: InputSignal<readonly ItemView<StepItem>[]> =
    input.required<readonly ItemView<StepItem>[]>();
  /** The current time, in epoch ms, from the page's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  private readonly _capacityService = inject(DispatchCapacityService);
  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _issueService = inject(IssueService);
  protected readonly board = inject(BoardStateService);

  protected readonly capacity = this._capacityService.capacity;
  protected readonly capacityError = this._capacityService.loadError;

  /** One entry per slot of the maximum: true while busy. */
  protected readonly pips: Signal<readonly boolean[]> = computed(() => {
    const capacity = this.capacity();
    if (capacity === null) {
      return [];
    }
    return Array.from({ length: capacity.maxConcurrent }, (_, i) => i < capacity.occupiedSlots);
  });

  protected readonly empty: Signal<boolean> = computed(
    () => this.workers().length === 0 && this.steps().length === 0,
  );

  // Running workers first, then the steps Foundry's code runs.
  protected readonly rows: Signal<readonly ItemView<WorkerItem | StepItem>[]> = computed(() => [
    ...this.workers(),
    ...this.steps(),
  ]);

  constructor() {
    // Refetch capacity whenever a run starts or ends, so the pips and slot count stay current.
    effect(() => {
      void this._workerSignalR.activeAgentCount();
      void this._issueService.liveIssueCount();
      untracked(() => void this._capacityService.load());
    });
    const handle = setInterval(() => void this._capacityService.load(), CAPACITY_REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(handle));
  }
}
