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
import {
  BoardCard,
  BoardCell,
  BoardColumn,
  CODE_STEP_LABELS,
  buildBoardColumns,
} from './board.model';

/** `confirm` shows a completed card's check mark; `collapse` folds it away before removal. */
export type CardPhase = 'live' | 'confirm' | 'collapse';

export interface BoardCardView extends BoardCard {
  readonly phase: CardPhase;
  /** The card just started needing a person; it pulses once. */
  readonly arrived: boolean;
  /** The card moved without a glide (reduced motion); it fades in instead. */
  readonly moved: boolean;
}

export interface BoardColumnView {
  readonly stage: BoardColumn['stage'];
  readonly queue: readonly BoardCardView[];
  readonly flow: readonly BoardCardView[];
  readonly needs: readonly BoardCardView[];
  readonly count: number;
}

interface Ghost {
  readonly card: BoardCard;
  readonly index: number;
  readonly phase: 'confirm' | 'collapse';
}

interface Batch {
  readonly issues: readonly IssueSummary[];
  readonly queueIds: readonly string[];
}

const DEBOUNCE_MS = 150;
const MAX_WAIT_MS = 500;
const ARRIVAL_MS = 1300;
const CONFIRM_MS = 1200;
// Matches --fd-motion-exit in styles/_motion.scss.
const COLLAPSE_MS = 180;
// Matches the reduced-motion fade on .board-card--moved.
const MOVED_FADE_MS = 200;
// A reload that moves this many cards snaps: a glide that busy reads as noise, not progress.
const MAX_GLIDING_CARDS = 12;
// A transition whose update never renders would hold the board; the browser times it out anyway.
const RENDER_WAIT_MS = 1000;
const CELLS: readonly BoardCell[] = ['queue', 'flow', 'needs'];

type ViewTransitionDocument = Document & {
  startViewTransition?: (update: () => Promise<void>) => ViewTransition;
};

function emptyColumns(): BoardColumn[] {
  return buildBoardColumns([], []);
}

function placementKeys(columns: readonly BoardColumn[]): Map<string, string> {
  const keys = new Map<string, string>();
  for (const column of columns) {
    for (const cell of CELLS) {
      column[cell].forEach((card, index) =>
        keys.set(card.issue.id, `${column.stage.id}|${cell}|${index}`),
      );
    }
  }
  return keys;
}

function cardsOf(columns: readonly BoardColumn[]): Map<string, BoardCard> {
  const cards = new Map<string, BoardCard>();
  for (const column of columns) {
    for (const cell of CELLS) {
      for (const card of column[cell]) {
        cards.set(card.issue.id, card);
      }
    }
  }
  return cards;
}

// A skipped transition rejects its promises with AbortError; that is expected, anything else is not.
function ignoreAbort(error: unknown): void {
  if (error instanceof DOMException && error.name === 'AbortError') {
    return;
  }
  console.error(error);
}

/**
 * Owns what the board shows: a snapshot of the issues that changes in batches, each batch one View
 * Transition so moved cards glide between cells, plus completion ghosts, needs-you arrivals and the
 * announcements that go with them. Provided per board page.
 */
@Injectable()
export class BoardStateService {
  private readonly _issueService = inject(IssueService);
  private readonly _signalR = inject(IssueSignalRService);
  private readonly _injector = inject(Injector);
  private readonly _document = inject(DOCUMENT) as ViewTransitionDocument;

  private readonly _rendered: WritableSignal<readonly BoardColumn[]> = signal(emptyColumns());
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

  readonly columns: Signal<readonly BoardColumnView[]> = computed(() => {
    const ghosts = this._ghosts();
    const arrived = this._arrived();
    const moved = this._moved();
    const view = (card: BoardCard, phase: CardPhase): BoardCardView => ({
      ...card,
      phase,
      arrived: phase === 'live' && arrived.has(card.issue.id),
      moved: moved.has(card.issue.id),
    });

    return this._rendered().map((column) => {
      const cells = {} as Record<BoardCell, BoardCardView[]>;
      for (const cell of CELLS) {
        const cards = column[cell].map((card) => {
          const ghost = ghosts.get(card.issue.id);
          return ghost === undefined ? view(card, 'live') : view(ghost.card, ghost.phase);
        });
        for (const [id, ghost] of ghosts) {
          const placed = cards.some((c) => c.issue.id === id);
          if (!placed && ghost.card.stage === column.stage.id && ghost.card.cell === cell) {
            cards.splice(Math.min(ghost.index, cards.length), 0, view(ghost.card, ghost.phase));
          }
        }
        cells[cell] = cards;
      }
      return { stage: column.stage, ...cells, count: column.count };
    });
  });

  readonly cardCount: Signal<number> = computed(() =>
    this._rendered().reduce((sum, c) => sum + c.count, 0),
  );
  readonly waitingCount: Signal<number> = computed(() =>
    this._rendered().reduce((sum, c) => sum + c.queue.length, 0),
  );
  readonly needsCount: Signal<number> = computed(() =>
    this._rendered().reduce((sum, c) => sum + c.needs.length, 0),
  );

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
      if (this._issueService.initialLoading()) {
        return;
      }
      untracked(() => this._enqueue({ issues, queueIds }));
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
    const moved = [...after].filter(([id, key]) => before.get(id) !== key).map(([id]) => id);
    const left = [...before.keys()].filter((id) => !after.has(id) && !this._ghosts().has(id));

    const previousCards = cardsOf(previous);
    const nextCards = [...cardsOf(next).values()];
    const arrivals = nextCards.filter(
      (card) => card.cell === 'needs' && previousCards.get(card.issue.id)?.cell !== 'needs',
    );
    const steps = nextCards.flatMap((card) => {
      const step = card.issue.codeStep;
      const changed = step !== null && previousCards.get(card.issue.id)?.issue.codeStep !== step;
      return changed ? [`#${card.issue.issueNumber} ${CODE_STEP_LABELS[step]}`] : [];
    });

    this._announce(arrivals, steps);

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

  private _build(batch: Batch): BoardColumn[] {
    for (const issue of batch.issues) {
      if (issue.stage === null && !this._warnedIds.has(issue.id)) {
        this._warnedIds.add(issue.id);
        console.warn(
          `Board: left out issue #${issue.issueNumber}, whose stage this dashboard does not know`,
        );
      }
    }
    return buildBoardColumns(batch.issues, batch.queueIds);
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

  private _announce(arrivals: readonly BoardCard[], steps: readonly string[]): void {
    const parts = [
      ...steps,
      ...arrivals.map(
        (card) => `#${card.issue.issueNumber} needs you: ${card.reason?.label ?? 'Needs you'}`,
      ),
      ...this._pendingCompletions.splice(0),
    ];
    if (parts.length > 0) {
      this._announcement.set(parts.join('. '));
    }
  }

  private _markArrivals(arrivals: readonly BoardCard[]): void {
    if (arrivals.length === 0) {
      return;
    }
    const ids = arrivals.map((card) => card.issue.id);
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
    for (const column of this._rendered()) {
      for (const cell of CELLS) {
        const index = column[cell].findIndex((card) => card.issue.id === updated.id);
        if (index < 0) {
          continue;
        }
        const card = column[cell][index];
        this._setGhost(updated.id, { card, index, phase: 'confirm' });
        this._pendingCompletions.push(`#${card.issue.issueNumber} completed`);
        this._after(CONFIRM_MS, () => {
          this._setGhost(updated.id, { card, index, phase: 'collapse' });
          this._after(COLLAPSE_MS, () => this._setGhost(updated.id, null));
        });
        return;
      }
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
