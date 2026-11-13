import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { LoadOutcome, combineOutcomes } from '../../../core/models/load-outcome';
import { ConnectionGateComponent } from '../../../shared/components/connection-gate/connection-gate';
import { IssueService } from '../../issues/issue.service';
import { IssueSummary } from '../../issues/issue.model';
import { DispatchCapacity, DispatchCapacityService } from '../dispatch-capacity.service';
import { FloorLaneComponent } from '../floor-lane/floor-lane';
import { FLOOR_QUEUE_LIMIT, FloorLane, PublishingSlot, buildFloorLanes, buildPublishingSlots } from '../floor.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';

/** Clock cadence for "time since last event"; token totals already arrive at most every 2 s per run. */
const CLOCK_INTERVAL_MS = 5_000;
// Safety net for occupancy changes no live signal reports (reservations, lowered limits).
const CAPACITY_REFRESH_MS = 30_000;

/** The Floor: one lane per active run, free dispatch slots, and the next queued issues. */
@Component({
  selector: 'fd-floor-page',
  imports: [ConnectionGateComponent, FloorLaneComponent, RoleTagComponent, SafeHrefPipe],
  templateUrl: './floor-page.html',
  styleUrl: './floor-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FloorPageComponent implements OnInit {
  private readonly _workerSignalR = inject(WorkerSignalRService);
  private readonly _issueService = inject(IssueService);
  private readonly _capacityService = inject(DispatchCapacityService);
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _injector = inject(Injector);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _fragment: Signal<string | null> = toSignal(inject(ActivatedRoute).fragment, {
    initialValue: null,
  });

  private readonly _nowSignal: WritableSignal<number> = signal(Date.now());
  protected readonly now: Signal<number> = this._nowSignal.asReadonly();

  /** What the Floor shows, refetched after a reconnect: the connection gate waits on it. */
  protected readonly reload = async (): Promise<LoadOutcome> =>
    combineOutcomes(
      await Promise.all([
        this._workerSignalR.loadActivity(),
        this._issueService.loadIssues(),
        this._capacityService.load(),
      ]),
    );

  protected readonly upToDate = (): string => 'Floor is up to date.';

  protected readonly lanes: Signal<FloorLane[]> = computed(() =>
    buildFloorLanes(
      this._workerSignalR.agentActivities(),
      this._issueService.issues(),
      !this._issueService.initialLoading() && this._issueService.loadError() === null,
      (workerRunId) => this._workerSignalR.recentToolsForRun(workerRunId),
    ));

  protected readonly capacity: Signal<DispatchCapacity | null> = this._capacityService.capacity;
  protected readonly capacityError: Signal<string | null> = this._capacityService.loadError;

  /** Runs whose agent has exited and whose commits Foundry is publishing (or retrying to publish). */
  protected readonly publishingSlots: Signal<PublishingSlot[]> = computed(() =>
    buildPublishingSlots(this._issueService.issues()));

  /**
   * Slots the server counts as occupied (reservations, starting runs) whose run has not reported
   * Agent Activity yet. A publishing run holds its dispatch slot until publishing finishes, so it is
   * not counted as starting.
   */
  protected readonly startingSlots: Signal<readonly number[]> = computed(() => {
    const occupied = this.capacity()?.occupiedSlots ?? 0;
    const starting = occupied - this.lanes().length - this.publishingSlots().length;
    return Array.from({ length: Math.max(0, starting) }, (_, i) => i);
  });

  /** One placeholder per free dispatch slot, as counted by the server. */
  protected readonly freeSlots: Signal<readonly number[]> = computed(() =>
    Array.from({ length: this.capacity()?.freeSlots ?? 0 }, (_, i) => i));

  protected readonly summary: Signal<string> = computed(() => {
    const working = this.lanes().length;
    const publishing = this.publishingSlots().length;
    const workingText = working === 1 ? '1 run active' : `${working} runs active`;
    const activeText = publishing > 0 ? `${workingText} · ${publishing} publishing` : workingText;
    const capacity = this.capacity();
    if (capacity === null) {
      return activeText;
    }
    const free = capacity.freeSlots === 1 ? '1 free slot' : `${capacity.freeSlots} free slots`;
    return `${activeText} · ${free} of ${capacity.maxConcurrent}`;
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
      untracked(() => void this._capacityService.load());
    });

    this._focusLaneFromFragment();

    const clockHandle = setInterval(() => this._nowSignal.set(Date.now()), CLOCK_INTERVAL_MS);
    const capacityHandle = setInterval(() => void this._capacityService.load(), CAPACITY_REFRESH_MS);
    this._destroyRef.onDestroy(() => {
      clearInterval(clockHandle);
      clearInterval(capacityHandle);
    });
  }

  ngOnInit(): void {
    void this._issueService.loadIssues();
  }

  /**
   * A board card's Watch on Floor link names a lane heading in the fragment. The router does no
   * anchor scrolling, so the page scrolls to the heading and focuses it once that lane renders; a
   * fragment naming no lane is ignored.
   */
  private _focusLaneFromFragment(): void {
    let handled: string | null = null;
    effect(() => {
      const fragment = this._fragment();
      const lanes = this.lanes();
      if (fragment === null || fragment === handled) {
        return;
      }
      if (!lanes.some((lane) => `floor-lane-${lane.issueId}` === fragment)) {
        return;
      }
      handled = fragment;
      afterNextRender(
        () => {
          const heading = this._host.ownerDocument.getElementById(fragment);
          if (heading === null || !this._host.contains(heading)) {
            return;
          }
          heading.scrollIntoView({ block: 'start' });
          heading.focus({ preventScroll: true });
        },
        { injector: this._injector },
      );
    });
  }
}
