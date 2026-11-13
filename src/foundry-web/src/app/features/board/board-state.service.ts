import {
  DestroyRef,
  Injectable,
  Injector,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { RepositorySummary } from '../settings/repositories/repository.model';
import { RepositoryService } from '../settings/repositories/repository.service';
import {
  BlockedItem,
  BoardBand,
  BoardBands,
  BoardItem,
  CODE_STEP_LABELS,
  ITEM_BANDS,
  NeedsItem,
  PipelineItem,
  RepoAlert,
  StepItem,
  WorkerItem,
  buildBoardBands,
  repoName,
} from './board.model';

/** `confirm` shows a completed card's check mark; `collapse` folds it away before removal. */
export type CardPhase = 'live' | 'confirm' | 'collapse';

/** An item as the board renders it, with its motion state. */
export type ItemView<T extends BoardItem = BoardItem> = T & {
  readonly phase: CardPhase;
  /** The card just started needing a person; it pulses once. */
  readonly arrived: boolean;
  /** The card changed band without a glide (reduced motion); it fades in instead. */
  readonly moved: boolean;
};

export interface BoardBandsView {
  readonly alerts: readonly RepoAlert[];
  readonly needs: readonly ItemView<NeedsItem>[];
  readonly workers: readonly ItemView<WorkerItem>[];
  readonly steps: readonly ItemView<StepItem>[];
  readonly pipeline: readonly ItemView<PipelineItem>[];
  readonly blocked: readonly ItemView<BlockedItem>[];
}

interface Ghost {
  readonly item: BoardItem;
  readonly index: number;
  readonly phase: 'confirm' | 'collapse';
}

interface Batch {
  readonly issues: readonly IssueSummary[];
  readonly queueIds: readonly string[];
  readonly repositories: readonly RepositorySummary[];
}

const DEBOUNCE_MS = 150;
const MAX_WAIT_MS = 500;
const ARRIVAL_MS = 1300;
const CONFIRM_MS = 1200;
// Matches --fd-motion-exit in styles/_motion.scss.
const COLLAPSE_MS = 180;
// Matches the reduced-motion fade on a moved card.
const MOVED_FADE_MS = 200;
// A reload that moves this many cards between bands snaps: a glide that busy reads as noise.
const MAX_GLIDING_CARDS = 12;
// A transition whose update never renders would hold the board; the browser times it out anyway.
const RENDER_WAIT_MS = 1000;

type ViewTransitionDocument = Document & {
  startViewTransition?: (update: () => Promise<void>) => ViewTransition;
};

const EMPTY_BANDS: BoardBands = buildBoardBands([], [], []);

function itemsOf(bands: BoardBands): BoardItem[] {
  return ITEM_BANDS.flatMap((band) => bands[band] as readonly BoardItem[]);
}

// Keyed by band only: a pipeline row that is renumbered has not moved.
function placementKeys(bands: BoardBands): Map<string, BoardBand> {
  return new Map(itemsOf(bands).map((item) => [item.issue.id, item.band]));
}

function itemsById(bands: BoardBands): Map<string, BoardItem> {
  return new Map(itemsOf(bands).map((item) => [item.issue.id, item]));
}

// A skipped transition rejects its promises with AbortError; that is expected, anything else is not.
function ignoreAbort(error: unknown): void {
  if (error instanceof DOMException && error.name === 'AbortError') {
    return;
  }
  console.error(error);
}

/**
 * Owns what the board shows: a snapshot of the bands that changes in batches, each batch one View
 * Transition so a card that changes band glides there, plus completion ghosts, needs-you arrivals
 * and the announcements that go with them. Provided per board page.
 */
@Injectable()
export class BoardStateService {
  private readonly _issueService = inject(IssueService);
  private readonly _repositoryService = inject(RepositoryService);
  private readonly _signalR = inject(IssueSignalRService);
  private readonly _injector = inject(Injector);
  private readonly _document = inject(DOCUMENT) as ViewTransitionDocument;

  private readonly _rendered: WritableSignal<BoardBands> = signal(EMPTY_BANDS);
  private readonly _ghosts: WritableSignal<ReadonlyMap<string, Ghost>> = signal(new Map());
  private readonly _arrived: WritableSignal<ReadonlySet<string>> = signal(new Set());
  private readonly _moved: WritableSignal<ReadonlySet<string>> = signal(new Set());
  private readonly _announcement: WritableSignal<string> = signal('');
  private readonly _glided: WritableSignal<boolean> = signal(false);

  /** Set by the page while the narrow sheet covers the board: a glide under it is never seen. */
  readonly sheetCoversBoard: WritableSignal<boolean> = signal(false);

  readonly stale: Signal<boolean> = computed(
    () => this._signalR.connectionStatus() !== 'connected',
  );

  /** Whether the latest batch glided, so a needs-you pulse waits for the glide to land. */
  readonly glided: Signal<boolean> = this._glided.asReadonly();

  /** Joined `#142 needs you: Failed. #139 completed` for the latest batch; for a polite live region. */
  readonly announcement: Signal<string> = this._announcement.asReadonly();

  readonly bands: Signal<BoardBandsView> = computed(() => {
    const rendered = this._rendered();
    const ghosts = this._ghosts();
    const arrived = this._arrived();
    const moved = this._moved();
    const view = <T extends BoardItem>(item: T, phase: CardPhase): ItemView<T> => ({
      ...item,
      phase,
      arrived: phase === 'live' && arrived.has(item.issue.id),
      moved: moved.has(item.issue.id),
    });
    const list = <T extends BoardItem>(items: readonly T[], band: BoardBand): ItemView<T>[] => {
      const views = items.map((item) => {
        const ghost = ghosts.get(item.issue.id);
        // A ghost keeps the item it completed from; it is the same band, so the same type.
        return ghost === undefined ? view(item, 'live') : view(ghost.item as T, ghost.phase);
      });
      for (const [id, ghost] of ghosts) {
        const placed = views.some((v) => v.issue.id === id);
        if (!placed && ghost.item.band === band) {
          views.splice(Math.min(ghost.index, views.length), 0, view(ghost.item as T, ghost.phase));
        }
      }
      return views;
    };

    return {
      alerts: rendered.alerts,
      needs: list(rendered.needs, 'needs'),
      workers: list(rendered.workers, 'workers'),
      steps: list(rendered.steps, 'steps'),
      pipeline: list(rendered.pipeline, 'pipeline'),
      blocked: list(rendered.blocked, 'blocked'),
    };
  });

  /** Every alert and item on the board. */
  readonly itemCount: Signal<number> = computed(() => {
    const rendered = this._rendered();
    return rendered.alerts.length + itemsOf(rendered).length;
  });

  private _firstApplied = false;
  private _snapNext = false;
  private _flushing = false;
  private _destroyed = false;
  private _pending: Batch | null = null;
  private _debounceHandle: ReturnType<typeof setTimeout> | null = null;
  private _maxWaitHandle: ReturnType<typeof setTimeout> | null = null;
  private readonly _timers = new Set<ReturnType<typeof setTimeout>>();
  private readonly _pendingCompletions: string[] = [];
  private readonly _warnedIds = new Set<string>();
  private readonly _onIssueUpdated = (updated: IssueSummary): void =>
    this._recordCompletion(updated);

  constructor() {
    effect(() => {
      const issues = this._issueService.issues();
      const queueIds = this._issueService.eligibleQueuedIssues().map((i) => i.id);
      const repositories = this._repositoryService.repositories();
      if (this._issueService.initialLoading()) {
        return;
      }
      untracked(() => this._enqueue({ issues, queueIds, repositories }));
    });

    effect(() => {
      if (this.stale()) {
        this._snapNext = true;
      }
    });

    this._signalR.on<IssueSummary>('IssueUpdated', this._onIssueUpdated);

    inject(DestroyRef).onDestroy(() => {
      this._destroyed = true;
      this._signalR.off<IssueSummary>('IssueUpdated', this._onIssueUpdated);
      this._clearBatchTimers();
      for (const handle of this._timers) {
        clearTimeout(handle);
      }
    });
  }

  private _enqueue(batch: Batch): void {
    if (!this._firstApplied) {
      this._firstApplied = true;
      this._rendered.set(this._build(batch));
      return;
    }

    this._pending = batch;
    if (this._debounceHandle !== null) {
      clearTimeout(this._debounceHandle);
    }
    this._debounceHandle = setTimeout(() => this._flush(), DEBOUNCE_MS);
    this._maxWaitHandle ??= setTimeout(() => this._flush(), MAX_WAIT_MS);
  }

  private _flush(): void {
    this._clearBatchTimers();
    // A running transition picks the pending batch up when it finishes: flushes chain, never overlap.
    if (this._flushing || this._pending === null || this._destroyed) {
      return;
    }
    const batch = this._pending;
    this._pending = null;
    this._flushing = true;
    void this._apply(batch).finally(() => {
      this._flushing = false;
      if (this._pending !== null) {
        this._flush();
      }
    });
  }

  private async _apply(batch: Batch): Promise<void> {
    const previous = this._rendered();
    const next = this._build(batch);

    const before = placementKeys(previous);
    const after = placementKeys(next);
    const moved = [...after].filter(([id, band]) => before.get(id) !== band).map(([id]) => id);
    const left = [...before.keys()].filter((id) => !after.has(id) && !this._ghosts().has(id));

    const previousItems = itemsById(previous);
    const arrivals = next.needs.filter(
      (item) => previousItems.get(item.issue.id)?.band !== 'needs',
    );
    this._announce(previous, next, previousItems, arrivals);

    const placementChanged = moved.length > 0 || left.length > 0;
    const snap = this._snapNext;
    if (!this.stale()) {
      this._snapNext = false;
    }

    const update = (): void => {
      this._rendered.set(next);
      this._markArrivals(arrivals);
    };

    if (
      placementChanged &&
      !snap &&
      moved.length + left.length <= MAX_GLIDING_CARDS &&
      this._canGlide()
    ) {
      this._glided.set(true);
      await this._glide(update);
      return;
    }

    this._glided.set(false);
    update();
    if (placementChanged && this._reducedMotion()) {
      this._markMoved(moved);
    }
  }

  private _build(batch: Batch): BoardBands {
    const bands = buildBoardBands(batch.issues, batch.queueIds, batch.repositories);
    for (const issue of bands.unplaced) {
      if (!this._warnedIds.has(issue.id)) {
        this._warnedIds.add(issue.id);
        console.warn(
          `Board: left out issue #${issue.issueNumber}, whose placement this dashboard does not know`,
        );
      }
    }
    return bands;
  }

  private _canGlide(): boolean {
    return (
      !this.stale() &&
      typeof this._document.startViewTransition === 'function' &&
      this._document.visibilityState === 'visible' &&
      !this._reducedMotion() &&
      !this.sheetCoversBoard()
    );
  }

  private _reducedMotion(): boolean {
    const view = this._document.defaultView;
    return (
      typeof view?.matchMedia === 'function' &&
      view.matchMedia('(prefers-reduced-motion: reduce)').matches
    );
  }

  private _glide(update: () => void): Promise<void> {
    const transition = this._document.startViewTransition!(() => {
      update();
      return new Promise<void>((resolve) => {
        const fallback = setTimeout(resolve, RENDER_WAIT_MS);
        afterNextRender(
          () => {
            clearTimeout(fallback);
            resolve();
          },
          { injector: this._injector },
        );
      });
    });
    transition.ready.catch(ignoreAbort);
    transition.updateCallbackDone.catch(ignoreAbort);
    return transition.finished.then(
      () => undefined,
      () => undefined,
    );
  }

  private _announce(
    previous: BoardBands,
    next: BoardBands,
    previousItems: ReadonlyMap<string, BoardItem>,
    arrivals: readonly NeedsItem[],
  ): void {
    const steps = itemsOf(next).flatMap((item) => {
      const step = item.issue.codeStep;
      const changed = step !== null && previousItems.get(item.issue.id)?.issue.codeStep !== step;
      return changed ? [`#${item.issue.issueNumber} ${CODE_STEP_LABELS[step]}`] : [];
    });
    const started = next.workers
      .filter((item) => previousItems.get(item.issue.id)?.band !== 'workers')
      .map((item) => `#${item.issue.issueNumber} started: ${item.role}`);
    const alerted = new Set(previous.alerts.map((alert) => alert.repositoryId));
    const alerts = next.alerts
      .filter((alert) => !alerted.has(alert.repositoryId))
      .map((alert) => `${repoName(alert.slug)} can't be worked on`);
    const parts = [
      ...alerts,
      ...steps,
      ...started,
      ...arrivals.map((item) => `#${item.issue.issueNumber} needs you: ${item.reason.label}`),
      ...this._pendingCompletions.splice(0),
    ];
    if (parts.length > 0) {
      this._announcement.set(parts.join('. '));
    }
  }

  private _markArrivals(arrivals: readonly NeedsItem[]): void {
    if (arrivals.length === 0) {
      return;
    }
    const ids = arrivals.map((item) => item.issue.id);
    this._arrived.update((current) => new Set([...current, ...ids]));
    this._after(ARRIVAL_MS, () =>
      this._arrived.update((current) => new Set([...current].filter((id) => !ids.includes(id)))),
    );
  }

  private _markMoved(ids: readonly string[]): void {
    this._moved.set(new Set(ids));
    this._after(MOVED_FADE_MS, () => this._moved.set(new Set()));
  }

  private _recordCompletion(updated: IssueSummary): void {
    if (updated.state !== 'completed' || this._ghosts().has(updated.id)) {
      return;
    }
    const rendered = this._rendered();
    for (const band of ITEM_BANDS) {
      const items = rendered[band] as readonly BoardItem[];
      const index = items.findIndex((item) => item.issue.id === updated.id);
      if (index < 0) {
        continue;
      }
      const item = items[index];
      this._setGhost(updated.id, { item, index, phase: 'confirm' });
      this._pendingCompletions.push(`#${item.issue.issueNumber} completed`);
      this._after(CONFIRM_MS, () => {
        this._setGhost(updated.id, { item, index, phase: 'collapse' });
        this._after(COLLAPSE_MS, () => this._setGhost(updated.id, null));
      });
      return;
    }
  }

  private _setGhost(id: string, ghost: Ghost | null): void {
    this._ghosts.update((current) => {
      const next = new Map(current);
      if (ghost === null) {
        next.delete(id);
      } else {
        next.set(id, ghost);
      }
      return next;
    });
  }

  private _after(ms: number, action: () => void): void {
    const handle = setTimeout(() => {
      this._timers.delete(handle);
      action();
    }, ms);
    this._timers.add(handle);
  }

  private _clearBatchTimers(): void {
    if (this._debounceHandle !== null) {
      clearTimeout(this._debounceHandle);
      this._debounceHandle = null;
    }
    if (this._maxWaitHandle !== null) {
      clearTimeout(this._maxWaitHandle);
      this._maxWaitHandle = null;
    }
  }
}
