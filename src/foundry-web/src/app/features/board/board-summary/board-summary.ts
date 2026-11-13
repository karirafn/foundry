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
import { RouterLink } from '@angular/router';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { ConnectionIndicatorComponent } from '../../../shared/components/connection-indicator/connection-indicator';
import { DispatchCapacityService } from '../../floor/dispatch-capacity.service';
import { IssueService } from '../../issues/issue.service';
import { DispatchControlsComponent } from '../dispatch-controls/dispatch-controls';

// Safety net for occupancy changes no live signal reports (reservations, lowered limits).
const CAPACITY_REFRESH_MS = 30_000;

/** The board's live summary: worker capacity, waiting and needs-you counts, dispatch control and connection. */
@Component({
  selector: 'fd-board-summary',
  imports: [ConnectionIndicatorComponent, DispatchControlsComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-summary.html',
  styleUrl: './board-summary.scss',
})
export class BoardSummaryComponent {
  readonly waitingCount: InputSignal<number> = input.required<number>();
  readonly needsCount: InputSignal<number> = input.required<number>();
  /** The person asked to go to the first card that needs them. */
  readonly needsYou: OutputEmitterRef<void> = output<void>();

  private readonly _capacityService = inject(DispatchCapacityService);
  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _issueService = inject(IssueService);
  protected readonly connectionStatus = inject(IssueSignalRService).connectionStatus;

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

  protected readonly needsText: Signal<string> = computed(() => {
    const count = this.needsCount();
    return count === 1 ? '1 needs you' : `${count} need you`;
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
