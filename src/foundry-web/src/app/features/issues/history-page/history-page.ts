import { ChangeDetectionStrategy, Component, ElementRef, OnInit, Signal, computed, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IssueService } from '../issue.service';
import { IssueCardComponent } from '../issue-card/issue-card';
import { IssueCardSkeletonComponent } from '../issue-card/issue-card-skeleton';
import { RunStatsBarComponent } from '../run-stats-bar/run-stats-bar';
import { WorkerRunTotalsService } from '../../workers/run-totals.service';
import { SideSheetComponent } from '../../../shared/components/side-sheet/side-sheet';
import { IssuePaneComponent, ISSUE_PANE_TITLE_ID } from '../../issue-pane/issue-pane';
import { IssueSelection, provideIssueSelection } from '../../issue-pane/issue-selection';

const SKELETON_COUNT = 4;
const EMPTY_MESSAGE = 'No resolved issues yet. Completed issues appear here.';

/** Resolved history: completed issues, newest first, with the run statistics of past runs. */
@Component({
  selector: 'fd-history-page',
  imports: [
    RouterLink,
    RunStatsBarComponent,
    IssueCardComponent,
    IssueCardSkeletonComponent,
    SideSheetComponent,
    IssuePaneComponent,
  ],
  providers: [provideIssueSelection('history')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './history-page.html',
  styleUrl: './history-page.scss',
})
export class HistoryPageComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly selection = inject(IssueSelection);
  private readonly _runTotalsService = inject(WorkerRunTotalsService);
  private readonly _resolvedHeading = viewChild.required<ElementRef<HTMLHeadingElement>>('resolvedHeading');

  protected readonly skeletonPlaceholders = Array.from({ length: SKELETON_COUNT }, (_, i) => i);
  protected readonly emptyMessage = EMPTY_MESSAGE;
  protected readonly paneTitleId = ISSUE_PANE_TITLE_ID;

  // The count before the latest Load more, so the announcement can name how many it added.
  private _preLoadMoreCount = 0;

  protected readonly announcement: Signal<string> = computed(() => {
    if (this.issueService.resolvedLoading()) {
      return 'Loading resolved issues…';
    }
    if (this.issueService.resolvedError() !== null) {
      return '';
    }
    const count = this.issueService.resolvedIssues().length;
    if (count === 0) {
      return EMPTY_MESSAGE;
    }
    const added = count - this._preLoadMoreCount;
    if (this._preLoadMoreCount > 0 && added > 0) {
      return `Loaded ${added} more resolved issue${added === 1 ? '' : 's'} (${count} total)`;
    }
    return `Loaded ${count} resolved issue${count === 1 ? '' : 's'}`;
  });

  ngOnInit(): void {
    this.issueService.loadResolved();
    this._runTotalsService.fetch();
  }

  protected loadMore(): void {
    this._preLoadMoreCount = this.issueService.resolvedIssues().length;
    this.issueService.loadMoreResolved();
  }

  protected retryLoad(): void {
    this._preLoadMoreCount = 0;
    this.issueService.retryResolvedFetch();
    this._resolvedHeading().nativeElement.focus();
  }

  protected retryLoadMore(): void {
    this.loadMore();
    this._resolvedHeading().nativeElement.focus();
  }
}
