import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { ConnectionStatus } from '../../../core/services/issue-signalr.service';
import { ConnectionIndicatorComponent } from '../../../shared/components/connection-indicator/connection-indicator';
import { IssueService } from '../../issues/issue.service';
import { IssueSummary } from '../../issues/issue.model';
import { DispatchCapacity, DispatchCapacityService } from '../dispatch-capacity.service';
import { FloorLaneComponent } from '../floor-lane/floor-lane';
import { FLOOR_QUEUE_LIMIT, FloorLane, buildFloorLanes } from '../floor.model';

/** Clock cadence for "time since last event"; token totals already arrive at most every 2 s per run. */
const CLOCK_INTERVAL_MS = 5_000;
// Safety net for occupancy changes no live signal reports (reservations, lowered limits).
const CAPACITY_REFRESH_MS = 30_000;

/** The Floor: one lane per active run, free dispatch slots, and the next queued issues. */
@Component({
  selector: 'fd-floor-page',
  imports: [FloorLaneComponent, ConnectionIndicatorComponent],
  templateUrl: './floor-page.html',
  styleUrl: './floor-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FloorPageComponent implements OnInit {
  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _issueService = inject(IssueService);
  private readonly _capacityService = inject(DispatchCapacityService);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _nowSignal: WritableSignal<number> = signal(Date.now());
  protected readonly now: Signal<number> = this._nowSignal.asReadonly();

  protected readonly connectionStatus: Signal<ConnectionStatus> = this._workerSignalR.connectionStatus;
  protected readonly stale: Signal<boolean> = computed(() => this.connectionStatus() !== 'connected');

  protected readonly lanes: Signal<FloorLane[]> = computed(() =>
    buildFloorLanes(
      this._workerSignalR.agentActivities(),
      this._issueService.issues(),
      !this._issueService.initialLoading() && this._issueService.loadError() === null,
      (workerRunId) => this._workerSignalR.recentToolsForRun(workerRunId),
    ));

  protected readonly capacity: Signal<DispatchCapacity | null> = this._capacityService.capacity;
  protected readonly capacityError: Signal<string | null> = this._capacityService.loadError;

  /**
   * Slots the server counts as occupied (reservations, starting runs) whose run has not reported
   * Agent Activity yet.
   */
  protected readonly startingSlots: Signal<readonly number[]> = computed(() => {
    const occupied = this.capacity()?.occupiedSlots ?? 0;
    return Array.from({ length: Math.max(0, occupied - this.lanes().length) }, (_, i) => i);
  });

  /** One placeholder per free dispatch slot, as counted by the server. */
  protected readonly freeSlots: Signal<readonly number[]> = computed(() =>
    Array.from({ length: this.capacity()?.freeSlots ?? 0 }, (_, i) => i));

  protected readonly summary: Signal<string> = computed(() => {
    const working = this.lanes().length;
    const workingText = working === 1 ? '1 run active' : `${working} runs active`;
    const capacity = this.capacity();
    if (capacity === null) {
      return workingText;
    }
    const free = capacity.freeSlots === 1 ? '1 free slot' : `${capacity.freeSlots} free slots`;
    return `${workingText} · ${free} of ${capacity.maxConcurrent}`;
  });

  // ADR 0067: eligibleQueuedIssues() preserves the server's dispatch order.
  private readonly _queued: Signal<IssueSummary[]> = this._issueService.eligibleQueuedIssues;
  protected readonly nextQueued: Signal<IssueSummary[]> = computed(() => this._queued().slice(0, FLOOR_QUEUE_LIMIT));
  protected readonly moreQueuedCount: Signal<number> = computed(() => Math.max(0, this._queued().length - FLOOR_QUEUE_LIMIT));
  protected readonly issuesLoading: Signal<boolean> = this._issueService.initialLoading;
  protected readonly issuesError: Signal<string | null> = this._issueService.loadError;

  constructor() {
    // Refetch capacity whenever a run starts or ends.
    effect(() => {
      void this._workerSignalR.activeAgentCount();
      void this._issueService.liveIssueCount();
      untracked(() => this._capacityService.load());
    });

    const clockHandle = setInterval(() => this._nowSignal.set(Date.now()), CLOCK_INTERVAL_MS);
    const capacityHandle = setInterval(() => this._capacityService.load(), CAPACITY_REFRESH_MS);
    this._destroyRef.onDestroy(() => {
      clearInterval(clockHandle);
      clearInterval(capacityHandle);
    });
  }

  ngOnInit(): void {
    this._issueService.loadIssues();
  }
}
