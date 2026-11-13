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
import { LoadOutcome, combineOutcomes } from '../../../core/models/load-outcome';
import { SettingsService } from '../../../core/services/settings.service';
import { ConnectionGateComponent } from '../../../shared/components/connection-gate/connection-gate';
import { DispatchCapacityService } from '../../floor/dispatch-capacity.service';
import { RepositoryService } from '../../settings/repositories/repository.service';
import { SideSheetComponent } from '../../../shared/components/side-sheet/side-sheet';
import { IssuePaneComponent, ISSUE_PANE_TITLE_ID } from '../../issue-pane/issue-pane';
import { IssueSelection, provideIssueSelection } from '../../issue-pane/issue-selection';
import { IssueService } from '../../issues/issue.service';
import { BlockedBandComponent } from '../blocked-band/blocked-band';
import { BoardRepositoriesService } from '../board-repositories.service';
import { BoardStateService } from '../board-state.service';
import { CardOpened, TRIGGER_LABEL, buildBoardBands } from '../board.model';
import { DispatchControlsComponent } from '../dispatch-controls/dispatch-controls';
import { NeedsBandComponent } from '../needs-band/needs-band';
import { PipelineBandComponent } from '../pipeline-band/pipeline-band';
import { ProgressBandComponent } from '../progress-band/progress-band';

// Matches $sheet-overlay-breakpoint in styles/_breakpoints.scss.
const NARROW_QUERY = '(width < 900px)';
const EMPTY_MESSAGE = `Nothing is in flight. Add the ${TRIGGER_LABEL} label to an issue to start work.`;
function goneMessage(issueNumber: number | null): string {
  return issueNumber === null
    ? 'The open issue is no longer on the board.'
    : `Issue #${issueNumber} is no longer on the board.`;
}

const STALE_QUEUE_MESSAGE = 'Queue order may be out of date';

/**
 * The landing page: active issues in priority bands (Needs you, In progress, Pipeline, Blocked),
 * with a pane for one issue's detail.
 */
@Component({
  selector: 'fd-board-page',
  imports: [
    ConnectionGateComponent,
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
  private readonly _repositoryList = inject(RepositoryService);
  private readonly _capacity = inject(DispatchCapacityService);
  private readonly _settings = inject(SettingsService);
  protected readonly selection = inject(IssueSelection);
  private readonly _document = inject(DOCUMENT);
  // Absent while the connection gate hides the board.
  private readonly _heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly triggerLabel = TRIGGER_LABEL;
  protected readonly paneTitleId = ISSUE_PANE_TITLE_ID;

  /** The board's one shared clock, so every card ticks together. */
  protected readonly now: Signal<number> = this.board.now;

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

  // Set by a reload that closed the pane; said with the up-to-date announcement.
  private _paneNote = '';

  /** What the board shows, refetched after a reconnect: the connection gate waits on it. */
  protected readonly reload = async (): Promise<LoadOutcome> => {
    const outcome = combineOutcomes(
      await Promise.all([
        this.issueService.loadIssues(),
        this._capacity.load(),
        this._settings.loadSettings(),
        this._repositories.reload(),
      ]),
    );
    if (outcome === 'loaded') {
      this.board.resync();
      const pane = this.selection.recheck();
      this._paneNote = pane.closed ? goneMessage(pane.issueNumber) : '';
    }
    return outcome;
  };

  protected readonly upToDate = (): string => {
    const bands = buildBoardBands(
      this.issueService.issues(),
      this.issueService.eligibleQueuedIssues().map((i) => i.id),
      this._repositoryList.repositories(),
    );
    const needs = bands.alerts.length + bands.needs.length;
    const status =
      needs === 0
        ? 'Board is up to date. Nothing needs you.'
        : `Board is up to date. ${needs} ${needs === 1 ? 'needs' : 'need'} you.`;
    const note = this._paneNote;
    this._paneNote = '';
    return note === '' ? status : `${note} ${status}`;
  };

  constructor() {
    effect(() => this.board.sheetCoversBoard.set(this.sheetCoversBoard()));
    this._watchNarrow();
  }

  ngOnInit(): void {
    void this.issueService.loadIssues();
    void this._repositories.reload();
  }

  protected openCard(opened: CardOpened): void {
    this.selection.open(opened.id, opened.invoker);
  }

  protected retryLoad(): void {
    void this.issueService.loadIssues();
    this._heading()?.nativeElement.focus();
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
