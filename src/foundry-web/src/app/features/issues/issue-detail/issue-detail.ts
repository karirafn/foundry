import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Observable } from 'rxjs';
import { IssueDetail } from '../issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { IssueService } from '../issue.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { providerTerminology } from '../../settings/accounts/provider.util';
import { getFailureCategoryDisplay } from '../../workers/failure-category';

@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, SafeHrefPipe, LogViewComponent],
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
        <a
          class="issue-detail__issue-link"
          [href]="d.url"
          target="_blank"
          rel="noopener noreferrer"
          [attr.aria-label]="'View issue #' + d.issueNumber + ' on provider'"
        >View issue</a>

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
                <span class="issue-detail__field-value">{{ s.blockedBy?.join(', ') }}</span>
              </div>
            }

            @if (d.state === 'ineligible') {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key" id="eligibility-violations-label">Eligibility Violations</span>
                @if (s.violations?.length) {
                  <ul class="issue-detail__violations" aria-labelledby="eligibility-violations-label">
                    @for (violation of s.violations!; track violation.rule) {
                      <li class="issue-detail__violation">{{ violation.description }}</li>
                    }
                  </ul>
                } @else {
                  <span class="issue-detail__field-value">Eligibility details are unavailable</span>
                }
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

        @if (d.state === 'ineligible') {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-eligibility-btn"
              type="button"
              [disabled]="_issueService.retryingEligibility()"
              [attr.aria-label]="'Retry eligibility check for issue #' + d.issueNumber"
              (click)="retryEligibility(d.id)"
            >{{ _issueService.retryingEligibility() ? 'Retrying...' : 'Retry Eligibility Check' }}</button>
          </div>
        }

        @if (d.state === 'failed' || d.state === 'continuable_failed' || d.state === 'revision_failed') {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-failed-btn"
              type="button"
              [disabled]="_issueService.retryingFailed()"
              [attr.aria-label]="'Retry failed issue #' + d.issueNumber"
              (click)="retryFailed(d.id)"
            >{{ _issueService.retryingFailed() ? 'Retrying Issue...' : 'Retry Issue' }}</button>
            <span
              class="issue-detail__retry-failed-error"
              role="alert"
            >{{ _issueService.retryFailedError() ?? '' }}</span>
            <span
              class="issue-detail__retry-success-announcement sr-only"
              aria-live="polite"
              aria-atomic="true"
            >{{ _issueService.retryFailedSuccess() ?? '' }}</span>
          </div>
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

  constructor() {
    effect(() => {
      const d = this.detail();
      const workerRunId = d?.stateDetails?.workerRunId ?? null;

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

  retryEligibility(id: string): void {
    this._issueService.retryEligibility(id);
  }

  retryFailed(id: string): void {
    this._issueService.retryFailed(id);
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
