import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, inject, viewChild } from '@angular/core';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { IssueCardComponent } from '../issue-card/issue-card';
import { IssueCardSkeletonComponent } from '../issue-card/issue-card-skeleton';
import { IssueDetailComponent } from '../issue-detail/issue-detail';
import { ConnectionIndicatorComponent } from '../../../shared/components/connection-indicator/connection-indicator';
import { DispatchControlsComponent } from './dispatch-controls/dispatch-controls';
import { IssueGroupFilterComponent } from '../issue-group-filter/issue-group-filter';
import { SettingsService } from '../../../core/services/settings.service';
import { LIVE_STATES } from '../issue.model';
import { RunStatsBarComponent } from '../run-stats-bar/run-stats-bar';
import { WorkerRunTotalsService } from '../../workers/run-totals.service';

const SKELETON_COUNT = 4;
const EMPTY_ACTIVE_MESSAGE = 'No active issues match the selected groups. Select Resolved to see closed work.';
const EMPTY_RESOLVED_MESSAGE = 'No resolved issues match the selected filters';
const STALE_MESSAGE = 'Queue order may be out of date';
const RESOLVED_HEADING_ID = 'resolved-band-heading';
const INELIGIBLE_QUEUE_HEADING_ID = 'ineligible-queue-heading';
const SECTION_HEADING_ID_PREFIX = 'issue-section-heading-';

@Component({
  selector: 'fd-issue-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    IssueCardComponent,
    IssueCardSkeletonComponent,
    IssueDetailComponent,
    ConnectionIndicatorComponent,
    DispatchControlsComponent,
    IssueGroupFilterComponent,
    RunStatsBarComponent,
  ],
  template: `
    <div class="issue-list">
      <header class="issue-list__header">
        <h1 #issueListHeadingEl class="issue-list__heading" tabindex="-1">Tracked Issues</h1>
        <fd-connection-indicator [status]="signalR.connectionStatus()" />
      </header>

      <fd-dispatch-controls />

      <div class="issue-list__layout">
        <fd-issue-group-filter class="issue-list__filters" />

        <div class="issue-list__body">
          <fd-run-stats-bar />

          @if (issueService.initialLoading()) {
            <div
              class="issue-list__skeletons"
              role="status"
              aria-busy="true"
              aria-live="polite"
            >
              <span class="sr-only">Loading tracked issues…</span>
              @for (placeholder of skeletonPlaceholders; track placeholder) {
                <fd-issue-card-skeleton />
              }
            </div>
          }

          @if (issueService.loadError()) {
            <div class="issue-list__error" role="alert">
              <span class="issue-list__error-message">Failed to load issues</span>
              <button
                class="issue-list__error-retry"
                type="button"
                (click)="retryActiveLoad()"
              >Retry</button>
            </div>
          }

          <!-- Persistent live-region announcer for stale queue-order state (always mounted).
               Text swaps in/out so NVDA+Firefox reliably announces the change.
               The visible strip below is @if-gated for appearance only and carries no role. -->
          <span
            role="status"
            class="issue-list__stale-announcer sr-only"
          >{{ staleAnnouncement() }}</span>

          @if (issueService.queueOrderStale()) {
            <div class="issue-list__stale-marker">
              <svg
                class="issue-list__stale-marker-icon"
                xmlns="http://www.w3.org/2000/svg"
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
                focusable="false"
              >
                <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
                <path d="M3 3v5h5" />
                <path d="M12 7v5l4 2" />
              </svg>
              <span class="issue-list__stale-marker-text">Queue order may be out of date</span>
            </div>
          }

          <!-- Persistent live-region announcer for empty-active state (always mounted). -->
          <p
            role="status"
            class="issue-list__empty-active-announcer sr-only"
          >{{ emptyActiveMessage() }}</p>

          @if (!issueService.initialLoading() && issueService.activeBandIssues().length === 0 && !issueService.loadError()) {
            <div class="issue-list__empty-active">
              <h2 class="issue-list__empty-active-heading">No active issues</h2>
              <p class="issue-list__empty-active-hint">{{ emptyActiveHint }}</p>
            </div>
          }

          @for (section of issueService.activeSections(); track section.group.id) {
            <section
              class="issue-list__section"
              [attr.data-group]="section.group.id"
              [attr.aria-labelledby]="sectionHeadingId(section.group.id)"
            >
              <div class="issue-list__section-header">
                <h2 [id]="sectionHeadingId(section.group.id)" class="issue-list__section-caption">{{ section.group.label }}</h2>
                <hr class="issue-list__section-hr" aria-hidden="true" />
              </div>

              <div class="issue-list__grid">
                @for (issue of section.issues; track issue.id) {
                  @if (issue.id === ineligibleQueueStartId()) {
                    <!-- H3: caption changed from h3 to p — no heading hierarchy skip (h1 → h3).
                         M1: role="group" + aria-labelledby makes the label effective on the container.
                         M2: guidance text is nested inside the labelled <p> so screen readers hear
                             exactly one boundary announcement — no duplicate sibling sr-only span. -->
                    <div
                      role="group"
                      class="issue-list__ineligible-queue-divider"
                      [attr.aria-labelledby]="ineligibleQueueHeadingId"
                    >
                      <hr class="issue-list__ineligible-queue-hr" aria-hidden="true" />
                      <p
                        [id]="ineligibleQueueHeadingId"
                        class="issue-list__ineligible-queue-caption"
                      >Not dispatchable<span class="sr-only"> — queued issues from repositories that are currently not dispatchable follow. Check repository settings to make them eligible.</span></p>
                    </div>
                  }
                  <div class="issue-list__item">
                    <fd-issue-card
                      [issue]="issue"
                      [expanded]="issueService.expandedIssueId() === issue.id"
                      [lastActivityAt]="isLiveIssue(issue.state) ? workerSignalR.activityForIssue(issue.id) : null"
                      [commitCount]="isLiveIssue(issue.state) ? workerSignalR.commitCountForIssue(issue.id) : null"
                      [agentActivity]="isLiveIssue(issue.state) ? workerSignalR.agentActivityForIssue(issue.id) : null"
                      [isNextUp]="issueService.nextUpIssueId() === issue.id"
                      [queuePosition]="issueService.queuePositions().get(issue.id) ?? null"
                      (toggle)="issueService.toggleExpand(issue.id)"
                    />

                    <div
                      class="issue-list__detail-wrapper"
                      [id]="'detail-' + issue.id"
                      [hidden]="issueService.expandedIssueId() !== issue.id"
                    >
                      @if (issueService.expandedIssueId() === issue.id) {
                        <fd-issue-detail
                          [detail]="issueService.issueDetail()"
                          [loading]="issueService.detailLoading()"
                          [error]="issueService.detailError()"
                          (retry)="issueService.loadDetail(issueService.expandedIssueId()!)"
                        />
                      }
                    </div>
                  </div>
                }
              </div>
            </section>
          }

          @if (issueService.resolvedSelected()) {
            <section
              class="issue-list__section issue-list__resolved-section"
              [attr.aria-labelledby]="resolvedHeadingId"
            >
              <div class="issue-list__section-header issue-list__resolved-divider">
                <h2 #resolvedBandHeadingEl [id]="resolvedHeadingId" class="issue-list__section-caption issue-list__resolved-caption" tabindex="-1">Resolved</h2>
                <hr class="issue-list__section-hr issue-list__resolved-hr" aria-hidden="true" />
              </div>

              @if (issueService.resolvedLoading()) {
                <div class="issue-list__resolved-loading" aria-hidden="true"></div>
              } @else if (issueService.resolvedError()) {
                <div class="issue-list__error" role="alert">
                  <span class="issue-list__error-message">Failed to load resolved issues</span>
                  <button
                    class="issue-list__error-retry"
                    type="button"
                    (click)="retryResolvedLoad()"
                  >Retry</button>
                </div>
              } @else if (issueService.resolvedIssues().length === 0) {
                <div class="issue-list__empty-resolved">
                  <p class="issue-list__empty-resolved-heading">{{ emptyResolvedMessage }}</p>
                </div>
              } @else {
                <div class="issue-list__resolved-band">
                  @for (issue of issueService.resolvedIssues(); track issue.id) {
                    <div class="issue-list__item">
                      <fd-issue-card
                        [issue]="issue"
                        [expanded]="issueService.expandedIssueId() === issue.id"
                        (toggle)="issueService.toggleExpand(issue.id)"
                      />

                      <div
                        class="issue-list__detail-wrapper"
                        [id]="'detail-' + issue.id"
                        [hidden]="issueService.expandedIssueId() !== issue.id"
                      >
                        @if (issueService.expandedIssueId() === issue.id) {
                          <fd-issue-detail
                            [detail]="issueService.issueDetail()"
                            [loading]="issueService.detailLoading()"
                            [error]="issueService.detailError()"
                            (retry)="issueService.loadDetail(issueService.expandedIssueId()!)"
                          />
                        }
                      </div>
                    </div>
                  }
                </div>
              }

              @if (issueService.resolvedLoadMoreError()) {
                <div class="issue-list__resolved-load-more-error" role="alert">
                  <span class="issue-list__resolved-load-more-error-message">{{ issueService.resolvedLoadMoreError() }}</span>
                  <button
                    class="issue-list__resolved-load-more-retry"
                    type="button"
                    (click)="retryLoadMore()"
                  >Retry</button>
                </div>
              } @else if (issueService.hasMoreResolved()) {
                <button
                  type="button"
                  class="issue-list__load-more"
                  [disabled]="issueService.resolvedLoadingMore()"
                  (click)="loadMore()"
                >
                  @if (issueService.resolvedLoadingMore()) {
                    <span class="sr-only">Loading more resolved issues…</span>
                  } @else {
                    Load more
                  }
                </button>
              }
            </section>
          }

          <!-- Persistent live-region announcer for resolved band (always mounted). -->
          <span
            aria-live="polite"
            class="issue-list__resolved-announcer sr-only"
          >{{ resolvedAnnouncement() }}</span>

          <!-- Persistent live-region announcer for next-up queue position (always mounted).
               Text is stable when the head is unchanged, so a reorder that doesn't change
               next-up produces no announcement; a next-up change updates the text → announces. -->
          <span
            role="status"
            class="issue-list__next-up-announcer sr-only"
          >{{ issueService.nextUpAnnouncement() }}</span>
        </div>
      </div>
    </div>
  `,
  styleUrl: './issue-list.scss',
})
export class IssueListComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly signalR = inject(IssueSignalRService);
  protected readonly workerSignalR = inject(WorkerSignalRService);
  private readonly _settingsService = inject(SettingsService);
  private readonly _runTotalsService = inject(WorkerRunTotalsService);

  private readonly issueListHeadingEl = viewChild.required<ElementRef<HTMLHeadingElement>>('issueListHeadingEl');
  private readonly resolvedBandHeadingEl = viewChild<ElementRef<HTMLHeadingElement>>('resolvedBandHeadingEl');

  protected readonly skeletonPlaceholders = Array.from({ length: SKELETON_COUNT }, (_, i) => i);
  protected readonly emptyActiveHint = EMPTY_ACTIVE_MESSAGE;
  protected readonly emptyResolvedMessage = EMPTY_RESOLVED_MESSAGE;
  protected readonly resolvedHeadingId = RESOLVED_HEADING_ID;
  protected readonly ineligibleQueueHeadingId = INELIGIBLE_QUEUE_HEADING_ID;

  /**
   * Id of the first rendered ineligible queued issue — the "Not dispatchable" sub-divider renders
   * just before it (inside the Waiting section, which holds every queued-tier state) — or null.
   */
  protected readonly ineligibleQueueStartId = computed<string | null>(() => {
    const ineligibleIds = new Set(this.issueService.ineligibleQueuedIssues().map(i => i.id));
    if (ineligibleIds.size === 0) {
      return null;
    }
    const first = this.issueService.activeBandIssues().find(i => ineligibleIds.has(i.id));
    return first?.id ?? null;
  });

  private _preLoadResolvedCount = 0;

  protected readonly staleAnnouncement = computed(() =>
    this.issueService.queueOrderStale() ? STALE_MESSAGE : ''
  );

  protected readonly emptyActiveMessage = computed(() => {
    if (
      !this.issueService.initialLoading() &&
      this.issueService.activeBandIssues().length === 0 &&
      !this.issueService.loadError()
    ) {
      return EMPTY_ACTIVE_MESSAGE;
    }
    return '';
  });

  protected readonly resolvedAnnouncement = computed(() => {
    if (this.issueService.resolvedLoading()) {
      return 'Loading resolved issues…';
    }
    const count = this.issueService.resolvedIssues().length;
    if (this.issueService.resolvedSelected() && count > 0) {
      const delta = count - this._preLoadResolvedCount;
      if (delta > 0 && this._preLoadResolvedCount > 0) {
        return `Loaded ${delta} more resolved issue${delta === 1 ? '' : 's'} (${count} total)`;
      }
      return `Loaded ${count} resolved issue${count === 1 ? '' : 's'}`;
    }
    if (
      this.issueService.resolvedSelected() &&
      !this.issueService.resolvedError() &&
      count === 0
    ) {
      return EMPTY_RESOLVED_MESSAGE;
    }
    return '';
  });

  protected sectionHeadingId(groupId: string): string {
    return SECTION_HEADING_ID_PREFIX + groupId;
  }

  protected isLiveIssue(state: string): boolean {
    return LIVE_STATES.has(state as never);
  }

  protected retryActiveLoad(): void {
    this.issueService.loadIssues();
    this.issueListHeadingEl().nativeElement.focus();
  }

  protected retryResolvedLoad(): void {
    this.issueService.retryResolvedFetch();
    this.resolvedBandHeadingEl()?.nativeElement.focus();
  }

  protected retryLoadMore(): void {
    this.loadMore();
    this.resolvedBandHeadingEl()?.nativeElement.focus();
  }

  loadMore(): void {
    this._preLoadResolvedCount = this.issueService.resolvedIssues().length;
    this.issueService.loadMoreResolved();
  }

  ngOnInit(): void {
    this.issueService.loadIssues();
    this.issueService.loadCounts();
    this._settingsService.loadSettings();
    this._runTotalsService.fetch();
  }
}
