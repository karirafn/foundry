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
import { StepChipComponent } from '../../../shared/components/step-chip/step-chip';
import { DispatchCapacityService } from '../../floor/dispatch-capacity.service';
import { IssueService } from '../../issues/issue.service';
import { BoardBandComponent } from '../board-band/board-band';
import { BoardRowComponent } from '../board-row/board-row';
import { ItemView } from '../board-state.service';
import { CODE_STEP_LABELS, CardOpened, StepItem, WorkerItem, codeStepTone } from '../board.model';
import { WorkerCardComponent } from './worker-card/worker-card';

// Safety net for occupancy changes no live signal reports (reservations, lowered limits).
const CAPACITY_REFRESH_MS = 30_000;

/** What runs now: worker cards, the steps Foundry runs itself, and the dispatch slots in use. */
@Component({
  selector: 'fd-progress-band',
  imports: [BoardBandComponent, BoardRowComponent, StepChipComponent, WorkerCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './progress-band.html',
  styleUrl: './progress-band.scss',
})
export class ProgressBandComponent {
  readonly workers: InputSignal<readonly ItemView<WorkerItem>[]> =
    input.required<readonly ItemView<WorkerItem>[]>();
  readonly steps: InputSignal<readonly ItemView<StepItem>[]> =
    input.required<readonly ItemView<StepItem>[]>();
  /** How many rows the Pipeline holds, for the empty band's line. */
  readonly queuedCount: InputSignal<number> = input.required<number>();
  /** The current time, in epoch ms, from the page's shared tick. */
  readonly now: InputSignal<number> = input.required<number>();
  readonly open: OutputEmitterRef<CardOpened> = output<CardOpened>();

  private readonly _capacityService = inject(DispatchCapacityService);
  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _issueService = inject(IssueService);

  protected readonly capacity = this._capacityService.capacity;
  protected readonly capacityError = this._capacityService.loadError;
  protected readonly stepLabels = CODE_STEP_LABELS;
  protected readonly stepTone = codeStepTone;

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

  // Never says why dispatch is held: the global banners own that.
  protected readonly emptyText: Signal<string> = computed(() => {
    const queued = this.queuedCount();
    return queued > 0 ? `No runs. ${queued} queued, waiting to dispatch` : 'Idle';
  });

  constructor() {
    // Refetch capacity whenever a run starts or ends, as the Floor does.
    effect(() => {
      void this._workerSignalR.activeAgentCount();
      void this._issueService.liveIssueCount();
      untracked(() => this._capacityService.load());
    });
    const handle = setInterval(() => this._capacityService.load(), CAPACITY_REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(handle));
  }
}
