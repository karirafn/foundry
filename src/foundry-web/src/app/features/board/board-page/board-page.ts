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
import { IssuePaneComponent, ISSUE_PANE_TITLE_ID } from '../../issue-pane/issue-pane';
import { IssueSelection, provideIssueSelection } from '../../issue-pane/issue-selection';
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
  imports: [BoardSummaryComponent, BoardColumnComponent, SideSheetComponent, IssuePaneComponent],
  providers: [BoardStateService, provideIssueSelection('board')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPageComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly board = inject(BoardStateService);
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  protected readonly selection = inject(IssueSelection);
  private readonly _document = inject(DOCUMENT);
  private readonly _heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly stageCount = STAGES.length;
  protected readonly triggerLabel = TRIGGER_LABEL;

  protected readonly paneTitleId = ISSUE_PANE_TITLE_ID;

  private readonly _narrow: WritableSignal<boolean> = signal(false);
  /** On narrow screens the sheet covers the board, so the board leaves the tab order. */
  protected readonly sheetCoversBoard: Signal<boolean> = computed(
    () => this._narrow() && this.selection.isOpen(),
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
  }

  ngOnInit(): void {
    this.issueService.loadIssues();
  }

  protected openCard(opened: CardOpened): void {
    this.selection.open(opened.id, opened.invoker);
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
