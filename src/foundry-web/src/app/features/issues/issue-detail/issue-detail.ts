import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Observable } from 'rxjs';
import { IssueDetail, IssueStateDetails } from '../issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { IssueService } from '../issue.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { providerTerminology } from '../../../shared/utils/provider.util';
import { rebaseOutcomeLabel } from '../rebase-outcome.format';
import { getFailureCategoryDisplay } from '../../../shared/utils/failure-category';
import {
  IssueState,
  MAX_CHECKS_FIX_ATTEMPTS,
  MAX_REBASE_CONFLICT_ATTEMPTS,
  RETRYABLE_STATES,
  REVISION_STATES,
  WORKING_STATES,
} from '../../../shared/utils/issue-state';

@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, SafeHrefPipe, LogViewComponent, ConfirmDialogComponent],
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
    } @else if (detail(); as d) {
      <div
        class="issue-detail__content"
        role="region"
        [attr.aria-label]="'Issue details for #' + d.issueNumber"
      >
        @if (d.url | safeHref; as safeIssueUrl) {
          <a
            class="issue-detail__issue-link"
            [href]="safeIssueUrl"
            target="_blank"
            rel="noopener noreferrer"
            [attr.aria-label]="'View issue #' + d.issueNumber + ' on provider'"
          >View issue</a>
        }

        @if (d.labels.length > 0) {
          <div class="issue-detail__labels">
            @for (label of d.labels; track label) {
              <span class="issue-detail__label-pill">{{ label }}</span>
            }
          </div>
        }

        @if (d.stateDetails; as s) {
          <div class="issue-detail__fields">
            @if (s.branchName) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Branch</span>
                <span class="issue-detail__branch issue-detail__field-value">{{ s.branchName }}</span>
              </div>
            }

            @if (s.pullRequestUrl | safeHref; as safePrUrl) {
              @let terms = _prTerminology(d.providerType);
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">{{ terms.pullRequest }}</span>
                <a
                  class="issue-detail__pr-link issue-detail__field-value"
                  [href]="safePrUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                  [attr.aria-label]="'Open ' + terms.pullRequest.toLowerCase() + ' for issue #' + d.issueNumber"
                >View {{ terms.prAbbrev }}</a>
              </div>
            }

            @if (s.workerRunId) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Worker Run</span>
                <span class="issue-detail__field-value">{{ s.workerRunId }}</span>
              </div>
            }

            @if (s.failureReason) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Failure Reason</span>
                <span class="issue-detail__field-value">{{ s.failureReason }}</span>
              </div>
            }

            @if (s.blockedBy?.length) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Blocked By</span>
                <span class="issue-detail__field-value">{{ s.blockedBy.join(', ') }}</span>
              </div>
            }

            @if (s.completedAt) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Completed</span>
                <span class="issue-detail__field-value">{{ s.completedAt | date: 'medium' }}</span>
              </div>
            }

            @if (s.failedAt) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Failed At</span>
                <span class="issue-detail__field-value">{{ s.failedAt | date: 'medium' }}</span>
              </div>
            }

            @if (s.feedbackCutoffAt) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Feedback Cutoff</span>
                <span class="issue-detail__field-value">{{ s.feedbackCutoffAt | date: 'medium' }}</span>
              </div>
            }

            @if (_rebaseOutcomeLabel(s); as rebaseLabel) {
              <div class="issue-detail__field issue-detail__rebase">
                <span class="issue-detail__field-key">Rebase</span>
                <span class="issue-detail__field-value">{{ rebaseLabel }}</span>
              </div>
            }
          </div>

          @if (s.checksOutcome === 'still_failing' && s.failingChecks?.length) {
            <div class="issue-detail__failing-checks">
              <span class="issue-detail__field-key" id="failing-checks-heading">Failing Checks</span>
              <ul class="issue-detail__failing-list" aria-labelledby="failing-checks-heading">
                @for (check of s.failingChecks; track check.name) {
                  <li class="issue-detail__failing-item">
                    <span class="issue-detail__failing-name">{{ check.name }}</span>
                    <span
                      class="badge issue-detail__failing-conclusion"
                      role="img"
                      [attr.aria-label]="'Conclusion: ' + check.conclusion"
                    >{{ check.conclusion }}</span>
                    @if (check.detailsUrl | safeHref; as safeUrl) {
                      <a
                        class="issue-detail__failing-link"
                        [href]="safeUrl"
                        target="_blank"
                        rel="noopener noreferrer"
                        [attr.aria-label]="'View details for failing check ' + check.name"
                      >View details</a>
                    }
                  </li>
                }
              </ul>
            </div>
          }

          <!-- Live region sits outside the grid so it does not create a phantom grid track
               when empty. It remains permanently mounted so screen readers detect content
               changes when retry data populates. -->
          <div
            class="issue-detail__retry-live-region"
            [class.issue-detail__retry-state]="s.transientRetry !== null"
            [class.issue-detail__retry-state--exhausted]="s.transientRetry?.isExhausted ?? false"
            [class.issue-detail__retry-live-region--empty]="s.transientRetry === null"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            @if (s.transientRetry; as retry) {
              <span class="issue-detail__field-key">Automatic Retry</span>
              <div class="issue-detail__retry-callout">
                @if (retry.isExhausted) {
                  <span class="badge issue-detail__retry-chip badge--retry-exhausted">Retry exhausted</span>
                  <span class="issue-detail__retry-message issue-detail__field-value">
                    Automatic retries exhausted after {{ retry.maxAttempts }} attempts.
                    @if (d.state === 'failed' || d.state === 'continuable_failed' || d.state === 'revision_failed') {
                      Use Retry Issue to try again manually.
                    }
                  </span>
                } @else {
                  <span class="badge issue-detail__retry-chip badge--retrying">Attempt {{ retry.attemptNumber }} of {{ retry.maxAttempts }}</span>
                  <span class="issue-detail__retry-message issue-detail__field-value">
                    @if (retry.nextAttemptDueAt) {
                      Automatic retry pending — next attempt at {{ retry.nextAttemptDueAt | date: 'medium' }}.
                    } @else {
                      Automatic retry pending — next attempt shortly.
                    }
                  </span>
                }
              </div>
            }
          </div>

          <!-- Revision kind callout. Permanently mounted like the retry live region so
               screen readers detect the transition into a revision and attempt-count
               increments. -->
          <div
            class="issue-detail__revision-live-region"
            [class.issue-detail__revision-state]="_showsRevisionCallout(d.state, s)"
            [class.issue-detail__revision-state--review]="s.revisionTriggerKind === 'review_feedback'"
            [class.issue-detail__revision-live-region--empty]="!_showsRevisionCallout(d.state, s)"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            @if (_showsRevisionCallout(d.state, s)) {
              <span class="issue-detail__field-key">Revision</span>
              <div class="issue-detail__revision-callout">
                @if (s.revisionTriggerKind === 'failed_checks') {
                  <span class="badge issue-detail__revision-chip badge--retrying"
                    >Fixing checks (attempt {{ s.checksFixAttempts ?? 1 }} of {{ _maxChecksFixAttempts }})</span>
                  <span class="issue-detail__revision-message issue-detail__field-value">
                    @if (d.state === 'revision_in_progress') {
                      Fixing checks — a worker is running.
                    } @else {
                      Fix run queued — checks failed on the PR.
                    }
                  </span>
                } @else if (s.revisionTriggerKind === 'rebase_conflict') {
                  <span class="badge issue-detail__revision-chip badge--retrying"
                    >Resolving conflicts (attempt {{ s.rebaseConflictAttempts ?? 1 }} of {{ _maxRebaseConflictAttempts }})</span>
                  <span class="issue-detail__revision-message issue-detail__field-value">
                    @if (d.state === 'revision_in_progress') {
                      Resolving conflicts with the default branch — a worker is running.
                    } @else {
                      Conflict-resolution run queued — the rebase onto the default branch conflicted.
                    }
                  </span>
                } @else {
                  <span class="badge issue-detail__revision-chip badge--revision-in-progress"
                    >Addressing review</span>
                  <span class="issue-detail__revision-message issue-detail__field-value">
                    @if (d.state === 'revision_in_progress') {
                      A worker is addressing reviewer feedback.
                    } @else {
                      Queued to address reviewer feedback.
                    }
                  </span>
                }
              </div>
            }
          </div>
        }

        <div class="issue-detail__fields issue-detail__fields--author">
          <div class="issue-detail__field issue-detail__field--full-width">
            <span class="issue-detail__field-key">Author</span>
            <span class="issue-detail__field-value">{{ d.author }}</span>
          </div>
        </div>

        @if (_workerRun(); as wr) {
          <div class="issue-detail__worker-run">
            @if (wr.failureCategory) {
              <div class="issue-detail__failure-category">
                <span
                  class="badge issue-detail__failure-chip"
                  [class]="_failureChipClass(wr.failureCategory)"
                  role="img"
                  [attr.aria-label]="'Failure category: ' + _failureCategoryLabel(wr.failureCategory)"
                >{{ _failureCategoryLabel(wr.failureCategory) }}</span>
                @if (wr.failureSummary) {
                  <span class="issue-detail__failure-summary">{{ wr.failureSummary }}</span>
                }
              </div>
            }

            @if (wr.state === 'completed' && wr.cancelRequestedAt) {
              <p class="issue-detail__cancel-note" role="status">
                The run finished before the cancel took effect.
              </p>
            }

            @if (wr.resultText) {
              <div class="issue-detail__result-text">
                <span class="issue-detail__field-key">Result</span>
                <p class="issue-detail__result-body">{{ wr.resultText }}</p>
              </div>
            }

            @if (wr.state === 'failed' && wr.hasStoredLog) {
              <fd-log-view
                mode="static"
                [lines]="_staticLogLines()"
                label="Run Log"
              />
            } @else if (wr.state === 'running') {
              <fd-log-view
                mode="stream"
                [logStream]="_liveLogStream()"
                label="Live Log"
              />
            }
          </div>
        }

        @if (_isWorking(d.state)) {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__cancel-btn"
              type="button"
              [disabled]="!_canCancel()"
              [attr.aria-label]="'Cancel run for issue #' + d.issueNumber"
              [attr.aria-describedby]="_cancelHint() ? 'cancel-run-hint-' + d.id : null"
              (click)="openCancelConfirm()"
            >{{ _cancelling() ? 'Cancelling…' : 'Cancel run' }}</button>
            @if (_cancelHint(); as hint) {
              <span class="issue-detail__cancel-hint" [id]="'cancel-run-hint-' + d.id">{{ hint }}</span>
            }
            <span
              class="issue-detail__retry-error"
              aria-live="assertive"
              aria-atomic="true"
            >{{ _cancelError() ?? '' }}</span>
          </div>
          <fd-confirm-dialog
            [open]="_confirmCancelOpen()"
            title="Stop the worker?"
            message="Commits on the branch are kept; Retry continues from them."
            confirmLabel="Stop worker"
            cancelLabel="Keep running"
            [destructive]="true"
            (confirmed)="confirmCancel()"
            (dismissed)="_confirmCancelOpen.set(false)"
          />
        }

        @if (_isRetryable(d.state)) {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-btn"
              type="button"
              [disabled]="_issueService.retrying()"
              [attr.aria-label]="'Retry issue #' + d.issueNumber"
              (click)="retryIssue(d.id)"
            >{{ _issueService.retrying() ? 'Retrying Issue...' : 'Retry Issue' }}</button>
            @if (d.state === 'continuable_failed') {
              <button
                class="issue-detail__retry-fresh-btn"
                type="button"
                [disabled]="_issueService.retrying()"
                [attr.aria-label]="'Retry issue #' + d.issueNumber + ' from a fresh branch'"
                (click)="_confirmRetryFreshOpen.set(true)"
              >Retry fresh</button>
            }
            <span
              class="issue-detail__retry-error"
              aria-live="assertive"
              aria-atomic="true"
            >{{ _issueService.retryError() ?? '' }}</span>
            <span
              class="issue-detail__retry-success-announcement sr-only"
              aria-live="polite"
              aria-atomic="true"
            >{{ _issueService.retrySuccess() ?? '' }}</span>
          </div>
          @if (d.state === 'continuable_failed') {
            <fd-confirm-dialog
              [open]="_confirmRetryFreshOpen()"
              title="Retry from a fresh branch?"
              message="The branch and all of its commits will be deleted on the provider, and any open pull request for it closed. The next worker starts over from the default branch. This cannot be undone."
              confirmLabel="Discard branch and retry"
              cancelLabel="Keep branch"
              [destructive]="true"
              (confirmed)="confirmRetryFresh(d.id)"
              (dismissed)="_confirmRetryFreshOpen.set(false)"
            />
          }
        }
      </div>
    }
  `,
  styleUrl: './issue-detail.scss',
})
export class IssueDetailComponent {
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly loading: InputSignal<boolean> = input.required<boolean>();
  readonly error: InputSignal<string | null> = input.required<string | null>();
  readonly retry: OutputEmitterRef<void> = output<void>();

  protected readonly _issueService = inject(IssueService);
  private readonly _workerRunService = inject(WorkerRunService);
  private readonly _workerSignalR = inject(WorkerSignalRService);

  private readonly _workerRunSignal: WritableSignal<WorkerRunDetail | null> = signal(null);
  protected readonly _workerRun: Signal<WorkerRunDetail | null> = this._workerRunSignal.asReadonly();

  private readonly _staticLogLinesSignal: WritableSignal<string[] | null> = signal(null);
  protected readonly _staticLogLines: Signal<string[] | null> = this._staticLogLinesSignal.asReadonly();

  private readonly _liveLogStreamSignal: WritableSignal<Observable<string> | null> = signal(null);
  protected readonly _liveLogStream: Signal<Observable<string> | null> = this._liveLogStreamSignal.asReadonly();

  protected readonly _confirmCancelOpen: WritableSignal<boolean> = signal(false);

  protected readonly _confirmRetryFreshOpen: WritableSignal<boolean> = signal(false);

  // Set once the user confirmed a cancel in this view; reset whenever the issue detail reloads
  // (an IssueUpdated push reloads it), after which the persisted cancelRequestedAt takes over.
  private readonly _cancelSubmitted: WritableSignal<boolean> = signal(false);

  private readonly _cancelErrorSignal: WritableSignal<string | null> = signal(null);
  protected readonly _cancelError: Signal<string | null> = this._cancelErrorSignal.asReadonly();

  protected readonly _cancelling: Signal<boolean> = computed(() => {
    const wr = this._workerRun();
    return this._cancelSubmitted() || (wr?.state === 'running' && wr.cancelRequestedAt !== null);
  });

  protected readonly _canCancel: Signal<boolean> = computed(
    () => this._workerRun()?.state === 'running' && !this._cancelling(),
  );

  protected readonly _cancelHint: Signal<string | null> = computed(() => {
    if (this._cancelling()) {
      return null;
    }
    const wr = this._workerRun();
    return wr === null || wr.state === 'starting' ? 'Starting…' : null;
  });

  constructor() {
    effect(() => {
      const d = this.detail();
      const workerRunId = d?.stateDetails?.workerRunId ?? null;

      this._cancelSubmitted.set(false);
      this._cancelErrorSignal.set(null);
      this._confirmCancelOpen.set(false);
      this._confirmRetryFreshOpen.set(false);

      if (workerRunId === null) {
        this._workerRunSignal.set(null);
        this._staticLogLinesSignal.set(null);
        this._liveLogStreamSignal.set(null);
        return;
      }

      this._workerRunService.getDetail(workerRunId).subscribe((wr) => {
        this._workerRunSignal.set(wr);

        if (wr === null) {
          return;
        }

        if (wr.state === 'failed' && wr.hasStoredLog) {
          this._workerRunService.getLog(workerRunId).subscribe((text) => {
            this._staticLogLinesSignal.set(text ? text.split('\n') : []);
          });
        } else if (wr.state === 'running') {
          this._liveLogStreamSignal.set(this._workerSignalR.streamLog(workerRunId));
        }
      });
    });
  }

  openCancelConfirm(): void {
    this._cancelErrorSignal.set(null);
    this._confirmCancelOpen.set(true);
  }

  confirmCancel(): void {
    this._confirmCancelOpen.set(false);
    const workerRunId = this._workerRun()?.workerRunId;

    if (!workerRunId) {
      return;
    }

    this._cancelSubmitted.set(true);
    this._workerRunService.requestCancel(workerRunId).subscribe((error) => {
      if (error !== null) {
        this._cancelSubmitted.set(false);
        this._cancelErrorSignal.set(error);
      }
    });
  }

  retryIssue(id: string): void {
    this._issueService.retryIssue(id);
  }

  confirmRetryFresh(id: string): void {
    this._confirmRetryFreshOpen.set(false);
    this._issueService.retryIssueFresh(id);
  }

  protected readonly _maxChecksFixAttempts = MAX_CHECKS_FIX_ATTEMPTS;
  protected readonly _maxRebaseConflictAttempts = MAX_REBASE_CONFLICT_ATTEMPTS;

  protected _isWorking(state: string): boolean {
    return WORKING_STATES.has(state as IssueState);
  }

  protected _isRetryable(state: string): boolean {
    return RETRYABLE_STATES.has(state as IssueState);
  }

  protected _showsRevisionCallout(state: string, s: IssueStateDetails): boolean {
    return REVISION_STATES.has(state as IssueState) && s.revisionTriggerKind != null;
  }

  protected _rebaseOutcomeLabel(s: IssueStateDetails): string | null {
    return rebaseOutcomeLabel(s.rebaseOutcome, s.rebaseSkippedReason);
  }

  protected _prTerminology(providerType: string): { pullRequest: string; prAbbrev: string } {
    return providerTerminology(providerType);
  }

  protected _failureCategoryLabel(category: string): string {
    return getFailureCategoryDisplay(category)?.label ?? category.toUpperCase().replace(/_/g, ' ');
  }

  protected _failureChipClass(category: string): string {
    return getFailureCategoryDisplay(category)?.cssClass ?? 'badge--failed';
  }

}
