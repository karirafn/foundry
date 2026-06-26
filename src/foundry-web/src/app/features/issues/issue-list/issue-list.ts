import { Component, OnInit, computed, inject } from '@angular/core';
import { IssueService } from '../issue.service';
import { IssueSignalRService } from '../../../core/services/issue-signalr.service';
import { IssueCardComponent } from '../issue-card/issue-card';
import { IssueCardSkeletonComponent } from '../issue-card/issue-card-skeleton';
import { IssueDetailComponent } from '../issue-detail/issue-detail';
import { ConnectionIndicatorComponent } from '../../../shared/components/connection-indicator/connection-indicator';
import { DispatchControlsComponent } from './dispatch-controls/dispatch-controls';
import { IssueFilterRailComponent } from '../issue-filter-rail/issue-filter-rail';
import { SettingsService } from '../../../features/settings/settings.service';

const SKELETON_COUNT = 4;
const EMPTY_ACTIVE_MESSAGE = 'No active issues match the current filters. Check the Resolved counts in the filter rail.';

@Component({
  selector: 'fd-issue-list',
  standalone: true,
  imports: [
    IssueCardComponent,
    IssueCardSkeletonComponent,
    IssueDetailComponent,
    ConnectionIndicatorComponent,
    DispatchControlsComponent,
    IssueFilterRailComponent,
  ],
  template: `
    <div class="issue-list">
      <header class="issue-list__header">
        <h1 class="issue-list__heading">Tracked Issues</h1>
        <fd-connection-indicator [status]="signalR.connectionStatus()" />
      </header>

      <fd-dispatch-controls />

      <div class="issue-list__layout">
        <aside class="issue-list__rail">
          <fd-issue-filter-rail />
        </aside>

        <div class="issue-list__body">
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
                (click)="issueService.loadIssues()"
              >Retry</button>
            </div>
          }

          <!-- Persistent live-region announcer for empty-active state (always mounted). -->
          <p
            role="status"
            class="issue-list__empty-active-announcer"
          >{{ emptyActiveMessage() }}</p>

          @if (!issueService.initialLoading() && issueService.activeBandIssues().length === 0 && !issueService.loadError()) {
            <div class="issue-list__empty-active">
              <h2 class="issue-list__empty-active-heading">No active issues</h2>
              <p class="issue-list__empty-active-hint">No active issues match the current filters. Check the Resolved counts in the filter rail.</p>
            </div>
          }

          <div class="issue-list__grid">
            @for (issue of issueService.activeBandIssues(); track issue.id; let idx = $index) {
              @if (issueService.liveIssueCount() > 0 && idx === issueService.liveIssueCount()) {
                <span class="sr-only">End of in-progress issues. Other issues follow.</span>
                <hr class="issue-list__separator" aria-hidden="true" />
              }
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

          @if (issueService.selectedResolvedStates().size > 0) {
            <div class="issue-list__resolved-section">
              <div class="issue-list__resolved-divider">
                <hr class="issue-list__resolved-hr" aria-hidden="true" />
                <span class="issue-list__resolved-caption">Resolved</span>
              </div>

              @if (issueService.resolvedLoading()) {
                <div class="issue-list__resolved-loading" role="status" aria-busy="true">
                  <span class="sr-only">Loading resolved issues…</span>
                </div>
              }

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

              @if (issueService.hasMoreResolved()) {
                <button
                  type="button"
                  class="issue-list__load-more"
                  [disabled]="issueService.resolvedLoadingMore()"
                  (click)="issueService.loadMoreResolved()"
                >
                  @if (issueService.resolvedLoadingMore()) {
                    <span class="sr-only">Loading more resolved issues…</span>
                  } @else {
                    Load more
                  }
                </button>
              }
            </div>
          }

          <!-- Persistent live-region announcer for resolved band (always mounted). -->
          <span
            aria-live="polite"
            class="issue-list__resolved-announcer sr-only"
          >{{ resolvedAnnouncement() }}</span>
        </div>
      </div>
    </div>
  `,
  styleUrl: './issue-list.scss',
})
export class IssueListComponent implements OnInit {
  protected readonly issueService = inject(IssueService);
  protected readonly signalR = inject(IssueSignalRService);
  private readonly _settingsService = inject(SettingsService);

  protected readonly skeletonPlaceholders = Array.from({ length: SKELETON_COUNT }, (_, i) => i);

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
    const count = this.issueService.resolvedIssues().length;
    if (this.issueService.selectedResolvedStates().size > 0 && count > 0) {
      return `Loaded ${count} resolved issue${count === 1 ? '' : 's'}`;
    }
    return '';
  });

  ngOnInit(): void {
    this.issueService.loadIssues();
    this.issueService.loadCounts();
    this._settingsService.loadSettings();
  }
}
