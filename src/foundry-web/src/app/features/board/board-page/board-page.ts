import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { SideSheetComponent } from '../../../shared/components/side-sheet/side-sheet';
import { IssueDetailComponent } from '../../issues/issue-detail/issue-detail';
import { IssueSummary } from '../../issues/issue.model';
import { IssueService } from '../../issues/issue.service';
import { BoardColumnComponent, CardOpened } from '../board-column/board-column';
import { BoardStateService } from '../board-state.service';
import { BoardSummaryComponent } from '../board-summary/board-summary';
import { STAGES, TRIGGER_LABEL } from '../board.model';

// Matches $board-breakpoint in styles/_board-breakpoints.scss.
const NARROW_QUERY = '(width < 900px)';
const EMPTY_MESSAGE = `Nothing is in flight. Add the ${TRIGGER_LABEL} label to an issue to start work.`;
const STALE_QUEUE_MESSAGE = 'Queue order may be out of date';

/** The landing page: active issues by stage, with a Needs-you row and a pane for one issue's detail. */
@Component({
  selector: 'fd-board-page',
  imports: [BoardSummaryComponent, BoardColumnComponent, SideSheetComponent, IssueDetailComponent],
  providers: [BoardStateService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPageComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly board = inject(BoardStateService);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _document = inject(DOCUMENT);
  private readonly _heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly stageCount = STAGES.length;
  protected readonly triggerLabel = TRIGGER_LABEL;

  protected readonly selectedId: WritableSignal<string | null> = signal(null);
  private readonly _openedIssue: WritableSignal<IssueSummary | null> = signal(null);
  private _invoker: HTMLElement | null = null;

  /** The open issue's latest summary; once it leaves the board (completed), the one it was opened with. */
  protected readonly selectedIssue: Signal<IssueSummary | null> = computed(() => {
    const id = this.selectedId();
    if (id === null) {
      return null;
    }
    return this.issueService.issues().find((i) => i.id === id) ?? this._openedIssue();
  });

  private readonly _narrow: WritableSignal<boolean> = signal(false);
  /** On narrow screens the sheet covers the board, so the board leaves the tab order. */
  protected readonly sheetCoversBoard: Signal<boolean> = computed(
    () => this._narrow() && this.selectedId() !== null,
  );

  protected readonly loading: Signal<boolean> = this.issueService.initialLoading;

  protected readonly empty: Signal<boolean> = computed(
    () =>
      !this.loading() &&
      this.issueService.loadError() === null &&
      this.board.columns().every((c) => c.queue.length + c.flow.length + c.needs.length === 0),
  );

  protected readonly emptyAnnouncement: Signal<string> = computed(() =>
    this.empty() ? EMPTY_MESSAGE : '',
  );
  protected readonly staleQueueAnnouncement: Signal<string> = computed(() =>
    this.issueService.queueOrderStale() ? STALE_QUEUE_MESSAGE : '',
  );

  constructor() {
    effect(() => this.board.sheetCoversBoard.set(this.sheetCoversBoard()));
    this._watchNarrow();
    inject(DestroyRef).onDestroy(() => {
      if (this.selectedId() !== null) {
        this.issueService.selectIssue(null);
      }
    });
  }

  ngOnInit(): void {
    this.issueService.loadIssues();
  }

  protected openCard(opened: CardOpened): void {
    this._invoker = opened.invoker;
    this._openedIssue.set(this.issueService.issues().find((i) => i.id === opened.id) ?? null);
    this.selectedId.set(opened.id);
    this.issueService.selectIssue(opened.id);
  }

  protected closeSheet(): void {
    const id = this.selectedId();
    if (id === null) {
      return;
    }
    this.selectedId.set(null);
    this.issueService.selectIssue(null);
    this._returnFocus(id);
  }

  protected reloadDetail(): void {
    const id = this.selectedId();
    if (id !== null) {
      this.issueService.loadDetail(id);
    }
  }

  protected retryLoad(): void {
    this.issueService.loadIssues();
    this._heading().nativeElement.focus();
  }

  /** Moves focus to the first card that needs a person, in flow order. */
  protected focusFirstNeedsCard(): void {
    const button = this._host.querySelector<HTMLElement>('[data-needs="true"] button');
    if (button === null) {
      return;
    }
    button.focus();
    button.scrollIntoView({ block: 'nearest' });
  }

  // The invoker may have re-rendered in another cell while the sheet was open; then its card's new
  // button takes focus, and the heading when the card left the board.
  private _returnFocus(id: string): void {
    const invoker = this._invoker;
    this._invoker = null;
    const target =
      (invoker?.isConnected ? invoker : null) ??
      this._host.querySelector<HTMLElement>(`[data-issue-card="${id}"] button`) ??
      this._heading().nativeElement;
    target.focus({ preventScroll: this._inView(target) });
  }

  private _inView(element: HTMLElement): boolean {
    const rect = element.getBoundingClientRect();
    const height = this._document.defaultView?.innerHeight ?? 0;
    return rect.top >= 0 && rect.bottom <= height;
  }

  private _watchNarrow(): void {
    const view = this._document.defaultView;
    if (typeof view?.matchMedia !== 'function') {
      return;
    }
    const query = view.matchMedia(NARROW_QUERY);
    this._narrow.set(query.matches);
    const onChange = (event: MediaQueryListEvent): void => this._narrow.set(event.matches);
    query.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', onChange));
  }
}
