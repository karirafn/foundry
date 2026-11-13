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
import { BlockedBandComponent } from '../blocked-band/blocked-band';
import { BoardRepositoriesService } from '../board-repositories.service';
import { BoardStateService } from '../board-state.service';
import { CardOpened, TRIGGER_LABEL } from '../board.model';
import { DispatchControlsComponent } from '../dispatch-controls/dispatch-controls';
import { NeedsBandComponent } from '../needs-band/needs-band';
import { PipelineBandComponent } from '../pipeline-band/pipeline-band';
import { ProgressBandComponent } from '../progress-band/progress-band';

// Matches $sheet-overlay-breakpoint in styles/_breakpoints.scss.
const NARROW_QUERY = '(width < 900px)';
const EMPTY_MESSAGE = `Nothing is in flight. Add the ${TRIGGER_LABEL} label to an issue to start work.`;
const STALE_QUEUE_MESSAGE = 'Queue order may be out of date';
// Cadence of "waiting 12m" and "running 3m"; coarse enough to stay quiet, fine enough to read live.
const CLOCK_INTERVAL_MS = 5_000;

/**
 * The landing page: active issues in priority bands (Needs you, In progress, Pipeline, Blocked),
 * with a pane for one issue's detail.
 */
@Component({
  selector: 'fd-board-page',
  imports: [
    DispatchControlsComponent,
    NeedsBandComponent,
    ProgressBandComponent,
    PipelineBandComponent,
    BlockedBandComponent,
    SideSheetComponent,
    IssuePaneComponent,
  ],
  providers: [BoardStateService, BoardRepositoriesService, provideIssueSelection('board')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
})
export class BoardPageComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly board = inject(BoardStateService);
  private readonly _repositories = inject(BoardRepositoriesService);
  protected readonly selection = inject(IssueSelection);
  private readonly _document = inject(DOCUMENT);
  private readonly _heading = viewChild.required<ElementRef<HTMLElement>>('heading');

  protected readonly triggerLabel = TRIGGER_LABEL;
  protected readonly paneTitleId = ISSUE_PANE_TITLE_ID;

  private readonly _now: WritableSignal<number> = signal(Date.now());
  protected readonly now: Signal<number> = this._now.asReadonly();

  private readonly _narrow: WritableSignal<boolean> = signal(false);
  /** On narrow screens the sheet covers the board, so the board leaves the tab order. */
  protected readonly sheetCoversBoard: Signal<boolean> = computed(
    () => this._narrow() && this.selection.isOpen(),
  );

  protected readonly loading: Signal<boolean> = this.issueService.initialLoading;

  protected readonly empty: Signal<boolean> = computed(
    () => !this.loading() && this.issueService.loadError() === null && this.board.itemCount() === 0,
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
    const clock = setInterval(() => this._now.set(Date.now()), CLOCK_INTERVAL_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(clock));
  }

  ngOnInit(): void {
    this.issueService.loadIssues();
    this._repositories.reload();
  }

  protected openCard(opened: CardOpened): void {
    this.selection.open(opened.id, opened.invoker);
  }

  protected retryLoad(): void {
    this.issueService.loadIssues();
    this._heading().nativeElement.focus();
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
