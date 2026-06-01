import {
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  InputSignal,
  OnDestroy,
  OutputEmitterRef,
  ViewChild,
  WritableSignal,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { IssueDetail } from '../issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { WorkerLogPanelComponent } from '../worker-log-panel/worker-log-panel';
import { WorkerLogService } from '../worker-log.service';

const LOG_PANEL_ID = 'issue-detail-log-panel';
const MOBILE_QUERY = '(max-width: 767px)';

const noopMediaQuery: MediaQueryList = {
  matches: false,
  media: MOBILE_QUERY,
  onchange: null,
  addEventListener: () => {},
  removeEventListener: () => {},
  addListener: () => {},
  removeListener: () => {},
  dispatchEvent: () => false,
} as unknown as MediaQueryList;

export const MEDIA_QUERY_FACTORY = new InjectionToken<(query: string) => MediaQueryList>(
  'MediaQueryFactory',
  {
    providedIn: 'root',
    factory: () =>
      typeof window !== 'undefined' && typeof window.matchMedia === 'function'
        ? (q: string) => window.matchMedia(q)
        : () => noopMediaQuery,
  }
);

@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  imports: [DatePipe, SafeHrefPipe, WorkerLogPanelComponent],
  template: `
    @if (error()) {
      <div class="issue-detail__error" role="alert">
        <span class="issue-detail__error-message">Failed to load details</span>
        <button
          class="issue-detail__error-retry"
          type="button"
          (click)="retry.emit()"
        >Retry</button>
      </div>
    } @else if (loading()) {
      <div class="issue-detail__skeleton" aria-busy="true" aria-label="Loading issue details">
        <div class="issue-detail__shimmer-bar" style="width: 100%"></div>
        <div class="issue-detail__shimmer-bar" style="width: 60%"></div>
        <div class="issue-detail__shimmer-bar" style="width: 40%"></div>
      </div>
    }

    @if (!error() && detail(); as d) {
      <div
        class="issue-detail__content"
        role="region"
        [attr.aria-label]="'Issue details for #' + d.issueNumber"
      >
        @if (d.body) {
          <div class="issue-detail__section">
            <p class="issue-detail__body">{{ d.body }}</p>
          </div>
        }

        @if (d.labels.length > 0) {
          <div class="issue-detail__section">
            <div class="issue-detail__labels">
              @for (label of d.labels; track label) {
                <span class="issue-detail__label-pill">{{ label }}</span>
              }
            </div>
          </div>
        }

        <div class="issue-detail__fields">
          @if (d.stateDetails.branchName) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Branch</span>
              <span class="issue-detail__branch issue-detail__field-value">{{ d.stateDetails.branchName }}</span>
            </div>
          }

          @if (d.stateDetails.pullRequestUrl | safeHref; as safePrUrl) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Pull Request</span>
              <a
                class="issue-detail__pr-link issue-detail__field-value"
                [href]="safePrUrl"
                target="_blank"
                rel="noopener noreferrer"
                [attr.aria-label]="'Open pull request for issue #' + d.issueNumber"
              >View PR</a>
            </div>
          }

          @if (d.stateDetails.workerRunId) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Worker Run</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.workerRunId }}</span>
            </div>
          }

          @if (d.stateDetails.failureReason) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Failure Reason</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.failureReason }}</span>
            </div>
          }

          @if (d.stateDetails.blockedBy) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Blocked By</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.blockedBy }}</span>
            </div>
          }

          @if (d.stateDetails.completedAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Completed</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.completedAt | date: 'medium' }}</span>
            </div>
          }

          @if (d.stateDetails.failedAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Failed At</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.failedAt | date: 'medium' }}</span>
            </div>
          }

          @if (d.stateDetails.feedbackCutoffAt) {
            <div class="issue-detail__field">
              <span class="issue-detail__field-key">Feedback Cutoff</span>
              <span class="issue-detail__field-value">{{ d.stateDetails.feedbackCutoffAt | date: 'medium' }}</span>
            </div>
          }

          <div class="issue-detail__field">
            <span class="issue-detail__field-key">Author</span>
            <span class="issue-detail__field-value">{{ d.author }}</span>
          </div>
        </div>

        @if (d.stateDetails.workerRunId) {
          <div class="issue-detail__log-section">
            <button
              #viewLogsBtn
              class="issue-detail__view-logs-btn"
              type="button"
              [attr.aria-expanded]="_logPanelOpen()"
              [attr.aria-controls]="_logPanelOpen() && !_isMobile() ? logPanelId : null"
              (click)="toggleLogPanel(d)"
            >
              <svg
                class="issue-detail__view-logs-icon"
                xmlns="http://www.w3.org/2000/svg"
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <polyline points="4 17 10 11 4 5"></polyline>
                <line x1="12" y1="19" x2="20" y2="19"></line>
              </svg>
              {{ _logPanelOpen() ? 'Hide Logs' : 'View Logs' }}
            </button>

            @if (_logPanelOpen() && !_isMobile()) {
              <div
                [id]="logPanelId"
                class="issue-detail__log-panel-inline"
              >
                <fd-worker-log-panel
                  [reports]="_logService.reports()"
                  [loading]="_logService.loading()"
                  [error]="_logService.error()"
                  [isLive]="_logService.isLive()"
                  (retry)="onLogRetry(d)"
                />
              </div>
            }
          </div>
        }
      </div>

      @if (_logPanelOpen() && _isMobile()) {
        <div
          class="issue-detail__overlay"
          role="dialog"
          aria-modal="true"
          aria-label="Worker log output"
          (keydown.escape)="closeLogPanel()"
          (click)="onOverlayBackdropClick($event)"
        >
          <span #focusTrapStart class="issue-detail__focus-sentinel" tabindex="0" aria-hidden="true" (focus)="focusOverlayClose()"></span>
          <div class="issue-detail__overlay-inner" (click)="$event.stopPropagation()">
            <div class="issue-detail__overlay-header">
              <span class="issue-detail__overlay-title">Worker Logs</span>
              <button
                #overlayCloseBtn
                class="issue-detail__overlay-close"
                type="button"
                aria-label="Close worker logs"
                (click)="closeLogPanel()"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="18"
                  height="18"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <line x1="18" y1="6" x2="6" y2="18"></line>
                  <line x1="6" y1="6" x2="18" y2="18"></line>
                </svg>
              </button>
            </div>
            <div class="issue-detail__overlay-body">
              <fd-worker-log-panel
                [reports]="_logService.reports()"
                [loading]="_logService.loading()"
                [error]="_logService.error()"
                [isLive]="_logService.isLive()"
                [hideHeader]="true"
                (retry)="onLogRetry(d)"
              />
            </div>
          </div>
          <span #focusTrapEnd class="issue-detail__focus-sentinel" tabindex="0" aria-hidden="true" (focus)="focusOverlayClose()"></span>
        </div>
      }
    }
  `,
  styleUrl: './issue-detail.scss',
})
export class IssueDetailComponent implements OnDestroy {
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly loading: InputSignal<boolean> = input.required<boolean>();
  readonly error: InputSignal<string | null> = input.required<string | null>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _logService = inject(WorkerLogService);
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _mqFactory = inject(MEDIA_QUERY_FACTORY);

  private readonly _mediaQuery = this._mqFactory(MOBILE_QUERY);

  protected readonly _logPanelOpen: WritableSignal<boolean> = signal(false);
  protected readonly _isMobile: WritableSignal<boolean> = signal(this._mediaQuery.matches);

  readonly logPanelId = LOG_PANEL_ID;

  @ViewChild('viewLogsBtn') private readonly _viewLogsBtn?: ElementRef<HTMLButtonElement>;
  @ViewChild('overlayCloseBtn') private readonly _overlayCloseBtn?: ElementRef<HTMLButtonElement>;

  constructor() {
    const handler = (e: MediaQueryListEvent) => this._isMobile.set(e.matches);
    this._mediaQuery.addEventListener('change', handler);
    this._destroyRef.onDestroy(() => this._mediaQuery.removeEventListener('change', handler));
  }

  ngOnDestroy(): void {
    this._logService.close();
  }

  toggleLogPanel(d: IssueDetail): void {
    if (this._logPanelOpen()) {
      this.closeLogPanel();
    } else {
      this._logPanelOpen.set(true);
      if (d.stateDetails.workerRunId) {
        this._logService.open(d.stateDetails.workerRunId, d.id, d.state);
      }
      if (this._isMobile()) {
        queueMicrotask(() => this._overlayCloseBtn?.nativeElement.focus());
      }
    }
  }

  closeLogPanel(): void {
    this._logPanelOpen.set(false);
    this._logService.close();
    this._viewLogsBtn?.nativeElement.focus();
  }

  onLogRetry(d: IssueDetail): void {
    if (d.stateDetails.workerRunId) {
      this._logService.open(d.stateDetails.workerRunId, d.id, d.state);
    }
  }

  onOverlayBackdropClick(event: Event): void {
    if (event.target === event.currentTarget) {
      this.closeLogPanel();
    }
  }

  focusOverlayClose(): void {
    this._overlayCloseBtn?.nativeElement.focus();
  }
}
