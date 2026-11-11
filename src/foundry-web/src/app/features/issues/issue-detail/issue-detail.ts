import {
  ChangeDetectionStrategy,
  ElementRef,
  Injector,
  afterNextRender,
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
  untracked,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Observable } from 'rxjs';
import {
  IssueDetail,
  IssueKindClassification,
  IssueStateDetails,
} from '../issue.model';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { IssueService, TriageAction } from '../issue.service';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { PUBLISH_RUN_STATES, WorkerRunDetail, WorkerRunState } from '../../workers/worker-run.model';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { PublishStatusComponent } from '../publish-status/publish-status';
import { PublishFactsComponent } from '../publish-facts/publish-facts';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { rebaseOutcomeLabel } from '../rebase-outcome.format';
import { getFailureCategoryDisplay } from '../../../shared/utils/failure-category';
import {
  IssueState,
  MAX_CHECKS_FIX_ATTEMPTS,
  MAX_REBASE_CONFLICT_ATTEMPTS,
  RETRIAGEABLE_STATES,
  RETRYABLE_STATES,
  REVISION_STATES,
  WORKING_STATES,
} from '../../../shared/utils/issue-state';

const KIND_LABELS: Readonly<Record<string, string>> = {
  feature: 'Feature',
  bug: 'Bug',
  refactor: 'Refactor',
  documentation: 'Documentation',
};

const KIND_SOURCE_NOTES: Readonly<Record<IssueKindClassification['source'], string>> = {
  labelled: 'From label',
  triaged: 'From triage',
  unclassified: 'No kind label, and triage has not classified it',
};

// States still waiting for a triage worker.
const TRIAGE_WAITING_STATES: ReadonlySet<string> = new Set<string>(['detected', 'triage_queued']);

const RETRIAGE_MESSAGE =
  'The issue leaves the queue and a triage worker reads it again. If it needs more detail, the questions are posted on the issue and it waits for a reply.';
const RETRIAGE_BLOCKED_SUFFIX = ' If its blockers are still open afterwards, it returns to Blocked.';

type TriageCalloutKind = 'waiting' | 'running' | 'failed' | 'needs_detail' | 'verdict' | 'not_triaged';

interface TriageCallout {
  readonly kind: TriageCalloutKind;
  readonly tint: 'neutral' | 'failed' | 'warning';
}

@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    SafeHrefPipe,
    LogViewComponent,
    ConfirmDialogComponent,
    RoleTagComponent,
    PublishStatusComponent,
    PublishFactsComponent,
  ],
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
        #contentRegion
        class="issue-detail__content"
        role="region"
        tabindex="-1"
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

        <div class="issue-detail__fields">
          @if (d.kind; as k) {
            <div class="issue-detail__field issue-detail__field--kind">
              <span class="issue-detail__field-key">Kind</span>
              <span class="issue-detail__kind-line">
                <span
                  class="issue-detail__field-value issue-detail__kind-value"
                  [class.issue-detail__kind-value--known]="k.source !== 'unclassified'"
                  [class.issue-detail__kind-value--unclassified]="k.source === 'unclassified'"
                >{{ k.source === 'unclassified' ? 'Unclassified' : _kindLabel(k.kind) }}</span>
                <span class="issue-detail__kind-source">{{ _kindSourceNote(k.source) }}</span>
              </span>
              @if (k.conflictingTriageKind; as triageKind) {
                <span class="issue-detail__kind-differs">
                  <span class="badge issue-detail__chip issue-detail__chip--warning">Triage differs</span>
                  <span class="issue-detail__field-value">
                    Triage read it as {{ _kindPhrase(triageKind) }}. The label decides; nothing changes.
                  </span>
                </span>
              }
            </div>
          }

          @if (d.stateDetails; as s) {
            @if (s.branchName) {
              <div class="issue-detail__field">
                <span class="issue-detail__field-key">Branch</span>
                <span class="issue-detail__branch issue-detail__field-value">{{ s.branchName }}</span>
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
          }
        </div>

        @if (d.stateDetails; as s) {
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

          @if (s.awaitingInput; as awaiting) {
            <div class="issue-detail__failing-checks issue-detail__awaiting-input">
              <span class="issue-detail__field-key" id="awaiting-input-heading">{{
                awaiting.resumeTarget === 'retriage' ? 'Questions from triage' : 'Open Questions'
              }}</span>
              <ul class="issue-detail__failing-list" aria-labelledby="awaiting-input-heading">
                @for (q of awaiting.questions; track $index) {
                  <li class="issue-detail__question">
                    <span class="issue-detail__field-value issue-detail__question-text">{{ q.question }}</span>
                    @if (q.context) {
                      <span class="issue-detail__field-key issue-detail__question-context">{{ q.context }}</span>
                    }
                  </li>
                }
              </ul>
              <div class="issue-detail__failing-item issue-detail__question-post" role="status" aria-live="polite">
                @switch (awaiting.postStatus) {
                  @case ('posted') {
                    <span class="issue-detail__field-value">
                      Posted {{ awaiting.postedAt | date: 'medium' }} — waiting for a reply.
                    </span>
                    @if (awaiting.commentUrl | safeHref; as commentHref) {
                      <a
                        class="issue-detail__failing-link issue-detail__question-link"
                        [href]="commentHref"
                        target="_blank"
                        rel="noopener noreferrer"
                      >View question</a>
                    }
                  }
                  @case ('failed') {
                    <span class="issue-detail__field-value issue-detail__question-post--failed">
                      Posting the questions failed: {{ awaiting.postFailureReason }}.
                      @if (awaiting.nextPostAttemptAt) {
                        Next attempt at {{ awaiting.nextPostAttemptAt | date: 'medium' }}.
                      }
                    </span>
                  }
                  @default {
                    <span class="issue-detail__field-value">Posting the questions…</span>
                  }
                }
              </div>
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
                    @if (d.state === 'triage_failed') {
                      Use Retry triage to run it again.
                    } @else if (d.state === 'failed' || d.state === 'continuable_failed' || d.state === 'revision_failed') {
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
                } @else if (s.revisionTriggerKind === 'input_provided') {
                  <span class="badge issue-detail__revision-chip badge--revision-in-progress"
                    >Resuming after input</span>
                  <span class="issue-detail__revision-message issue-detail__field-value">
                    @if (d.state === 'revision_in_progress') {
                      A worker is applying the answers to the open questions.
                    } @else {
                      Queued to apply the answers to the open questions.
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

          <!-- Publish callout: publishing, retrying, exhausted or branch diverged. Its live region
               is permanently mounted inside fd-publish-status. -->
          <fd-publish-status
            [issueState]="d.state"
            [run]="_workerRun()"
            [stateDetails]="s"
            [providerType]="d.providerType ?? ''"
          />
        }

        <!-- Triage callout. Permanently mounted like the retry and revision live regions so
             screen readers detect triage transitions; it is also where focus goes after a
             triage action succeeds. It sits outside the state-details block because the
             waiting and not-triaged states carry no state details. -->
        @let callout = _triageCallout(d);
        <div
          #triageRegion
          class="issue-detail__triage-live-region"
          [class.issue-detail__triage-state]="callout !== null"
          [class.issue-detail__triage-state--neutral]="callout !== null && callout.tint === 'neutral'"
          [class.issue-detail__triage-state--failed]="callout?.tint === 'failed'"
          [class.issue-detail__triage-state--warning]="callout?.tint === 'warning'"
          [class.issue-detail__triage-live-region--empty]="callout === null"
          role="status"
          aria-live="polite"
          aria-atomic="true"
          tabindex="-1"
        >
          @if (callout !== null) {
            <span class="issue-detail__field-key">Triage</span>
            <div class="issue-detail__triage-callout">
              @switch (callout.kind) {
                @case ('waiting') {
                  <span class="issue-detail__field-value issue-detail__triage-message">Waiting for a worker to triage this issue.</span>
                }
                @case ('running') {
                  <span class="issue-detail__field-value issue-detail__triage-message"
                    >A triage worker is reading the issue and the repository.</span>
                }
                @case ('failed') {
                  <span class="issue-detail__field-value issue-detail__triage-message">
                    <strong>Triage did not finish.</strong>
                    Retry triage to run it again, or skip triage to queue the issue without a readiness check.
                  </span>
                }
                @case ('needs_detail') {
                  <span class="issue-detail__field-value issue-detail__triage-message">
                    Triage needs more detail. The questions are posted on the issue; once a reply settles,
                    Foundry triages again.
                  </span>
                }
                @case ('verdict') {
                  @if (d.triage; as t) {
                    <span
                      class="badge issue-detail__chip"
                      [class.issue-detail__chip--success]="t.outcome === 'ready'"
                      [class.issue-detail__chip--warning]="t.outcome === 'needs_input'"
                    >{{ t.outcome === 'ready' ? 'Ready' : 'Needs input' }}</span>
                    <span class="issue-detail__field-value">{{ _kindLabel(t.kind) }}</span>
                    <span class="issue-detail__field-value issue-detail__triage-message">Triaged {{ t.decidedAt | date: 'medium' }}</span>
                    @if (t.outcome === 'needs_input') {
                      <span class="issue-detail__field-value issue-detail__triage-message">{{ _questionsAsked(t.questions.length) }}</span>
                    }
                  }
                }
                @case ('not_triaged') {
                  <span class="issue-detail__field-value issue-detail__triage-message"
                    >Not triaged. This issue was queued without a readiness check.</span>
                }
              }
            </div>
          }
        </div>

        <div class="issue-detail__fields issue-detail__fields--author">
          <div class="issue-detail__field issue-detail__field--full-width">
            <span class="issue-detail__field-key">Author</span>
            <span class="issue-detail__field-value">{{ d.author }}</span>
          </div>
        </div>

        @if (_workerRun(); as wr) {
          <div class="issue-detail__worker-run">
            @if (wr.publish; as published) {
              <fd-publish-facts
                [facts]="published"
                [branchName]="d.stateDetails?.branchName ?? null"
                [providerType]="d.providerType ?? ''"
              />
            }
            <div class="issue-detail__run-header">
              <span class="issue-detail__field-key">Run</span>
              <fd-role-tag [role]="wr.role" />
            </div>

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

            <!-- Publish states show no log view: the agent has stopped. -->
            @if (!_isPublishRunState(wr.state)) {
              @if (wr.hasStoredLog) {
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
            }
          </div>
        }

        <!-- Cancel run is hidden while the run publishes, so a push is never cut off mid-way. -->
        @if (_isWorking(d.state) && !d.publishPhase && _showsCancel()) {
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
            [title]="d.state === 'triage_in_progress' ? 'Stop the triage worker?' : 'Stop the worker?'"
            [message]="
              d.state === 'triage_in_progress'
                ? 'The issue moves to Triage failed, where you can retry or skip triage.'
                : _implementCancelMessage
            "
            confirmLabel="Stop worker"
            cancelLabel="Keep running"
            [destructive]="true"
            (confirmed)="confirmCancel()"
            (dismissed)="_confirmCancelOpen.set(false)"
          />
        }

        @if (d.state === 'publish_failed') {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-btn"
              type="button"
              [disabled]="_publishRetryPending()"
              [attr.aria-label]="'Retry publishing for issue #' + d.issueNumber"
              (click)="openPublishRetryConfirm()"
            >{{ _publishRetryPending() ? 'Retrying publish…' : 'Retry publish' }}</button>
            <span
              class="issue-detail__retry-error"
              aria-live="assertive"
              aria-atomic="true"
            >{{ _publishRetryError() ?? '' }}</span>
          </div>
          <!-- Not destructive, but pushing triggers CI with repository secrets, so confirm first. -->
          <fd-confirm-dialog
            [open]="_confirmPublishRetryOpen()"
            title="Retry publishing?"
            [message]="_publishRetryMessage(d.stateDetails?.branchName ?? null)"
            confirmLabel="Retry publish"
            cancelLabel="Not now"
            [destructive]="false"
            (confirmed)="confirmPublishRetry()"
            (dismissed)="_confirmPublishRetryOpen.set(false)"
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

        @if (d.state === 'triage_failed') {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-btn"
              type="button"
              [disabled]="_triagePending() !== null"
              [attr.aria-label]="'Retry triage for issue #' + d.issueNumber"
              (click)="retryTriage(d.id)"
            >{{ _triagePending() === 'retry_triage' ? 'Retrying triage…' : 'Retry triage' }}</button>
            <button
              class="issue-detail__secondary-btn"
              type="button"
              [disabled]="_triagePending() !== null"
              [attr.aria-label]="'Skip triage for issue #' + d.issueNumber"
              (click)="_confirmSkipTriageOpen.set(true)"
            >{{ _triagePending() === 'skip_triage' ? 'Skipping triage…' : 'Skip triage' }}</button>
            <span
              class="issue-detail__retry-error"
              aria-live="assertive"
              aria-atomic="true"
            >{{ _issueService.triageActionError() ?? '' }}</span>
          </div>
          <fd-confirm-dialog
            [open]="_confirmSkipTriageOpen()"
            [title]="'Skip triage for #' + d.issueNumber + '?'"
            message="The issue is queued without a readiness check. If it has open blockers, it waits as Blocked first. Its kind stays as it is now."
            confirmLabel="Skip triage"
            cancelLabel="Cancel"
            [destructive]="false"
            (confirmed)="confirmSkipTriage(d.id)"
            (dismissed)="_confirmSkipTriageOpen.set(false)"
          />
        }

        @if (_isRetriageable(d.state)) {
          <div class="issue-detail__actions">
            <button
              class="issue-detail__retry-btn"
              type="button"
              [disabled]="_triagePending() !== null"
              [attr.aria-label]="'Re-triage issue #' + d.issueNumber"
              (click)="_confirmRetriageOpen.set(true)"
            >{{ _triagePending() === 'retriage' ? 'Re-triaging…' : 'Re-triage' }}</button>
            <span
              class="issue-detail__retry-error"
              aria-live="assertive"
              aria-atomic="true"
            >{{ _issueService.triageActionError() ?? '' }}</span>
          </div>
          <fd-confirm-dialog
            [open]="_confirmRetriageOpen()"
            [title]="'Triage #' + d.issueNumber + ' again?'"
            [message]="_retriageMessage(d.state)"
            confirmLabel="Re-triage"
            cancelLabel="Cancel"
            [destructive]="false"
            (confirmed)="confirmRetriage(d.id)"
            (dismissed)="_confirmRetriageOpen.set(false)"
          />
        }

        <!-- Outside the action rows: a successful triage action reloads the detail into a state
             without those rows, and the announcement must survive that. -->
        <span
          class="issue-detail__triage-success-announcement sr-only"
          aria-live="polite"
          aria-atomic="true"
        >{{ _issueService.triageActionSuccess() ?? '' }}</span>
        <!-- Likewise outside the Retry publish row, which unmounts once the publish restarts. -->
        <span
          class="issue-detail__publish-retry-success-announcement sr-only"
          aria-live="polite"
          aria-atomic="true"
        >{{ _publishRetrySuccess() ?? '' }}</span>
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

  protected readonly _confirmSkipTriageOpen: WritableSignal<boolean> = signal(false);

  protected readonly _confirmRetriageOpen: WritableSignal<boolean> = signal(false);

  protected readonly _triagePending: Signal<TriageAction | null> = computed(() =>
    this._issueService.triageActionPending(),
  );

  private readonly _injector = inject(Injector);

  private readonly _triageRegion: Signal<ElementRef<HTMLElement> | undefined> =
    viewChild<ElementRef<HTMLElement>>('triageRegion');

  // Set when a triage action succeeds; consumed by the detail effect once the reloaded detail
  // arrives, which then focuses the triage callout.
  private _focusTriageRegionAfterReload = false;

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

  /** Cancel run shows while the run starts or runs, or before its detail has loaded. */
  protected readonly _showsCancel: Signal<boolean> = computed(() => {
    const state = this._workerRun()?.state;
    return state === undefined || state === 'starting' || state === 'running';
  });

  // On SIGTERM the worker bundles its work and the Publisher pushes it.
  protected readonly _implementCancelMessage = "Foundry publishes the run's commits to the branch; Retry continues from them.";

  protected readonly _confirmPublishRetryOpen: WritableSignal<boolean> = signal(false);

  // Pending, error and success are local to this view, like the cancel signals.
  private readonly _publishRetryPendingSignal: WritableSignal<boolean> = signal(false);
  protected readonly _publishRetryPending: Signal<boolean> = this._publishRetryPendingSignal.asReadonly();

  private readonly _publishRetryErrorSignal: WritableSignal<string | null> = signal(null);
  protected readonly _publishRetryError: Signal<string | null> = this._publishRetryErrorSignal.asReadonly();

  // Not reset by the detail-reload effect, so it survives the IssueUpdated reload it causes.
  private readonly _publishRetrySuccessSignal: WritableSignal<string | null> = signal(null);
  protected readonly _publishRetrySuccess: Signal<string | null> = this._publishRetrySuccessSignal.asReadonly();

  private readonly _contentRegion: Signal<ElementRef<HTMLElement> | undefined> =
    viewChild<ElementRef<HTMLElement>>('contentRegion');

  // Set when a publish retry is accepted; once the Retry publish button unmounts, focus moves to
  // the content region so it does not fall back to <body>.
  private _focusContentWhenPublishRetryGone = false;

  constructor() {
    // A successful triage action reloads the detail into a new state that no longer has the
    // clicked button; remember to move focus to the triage callout once that reload lands
    // (WCAG 2.4.3). Registered before the detail effect so the flag is set first.
    effect(() => {
      if (this._issueService.triageActionSuccess() !== null) {
        this._focusTriageRegionAfterReload = true;
      }
    });

    effect(() => {
      const d = this.detail();
      const workerRunId = d?.stateDetails?.workerRunId ?? null;

      this._cancelSubmitted.set(false);
      this._cancelErrorSignal.set(null);
      this._confirmCancelOpen.set(false);
      this._confirmRetryFreshOpen.set(false);
      this._confirmSkipTriageOpen.set(false);
      this._confirmRetriageOpen.set(false);
      this._confirmPublishRetryOpen.set(false);
      this._publishRetryErrorSignal.set(null);

      if (d !== null && untracked(() => this._focusTriageRegionAfterReload)) {
        this._focusTriageRegionAfterReload = false;
        afterNextRender(() => this._triageRegion()?.nativeElement.focus(), { injector: this._injector });
      }

      if (d !== null) {
        this._focusContentIfPublishRetryGone(d.state);
      }

      if (workerRunId === null) {
        this._workerRunSignal.set(null);
        this._staticLogLinesSignal.set(null);
        this._liveLogStreamSignal.set(null);
        return;
      }

      this._workerRunService.getDetail(workerRunId).subscribe((wr) => {
        this._workerRunSignal.set(wr);

        // Publish states show no log view, so there is no log to fetch or stream.
        if (wr === null || PUBLISH_RUN_STATES.has(wr.state)) {
          return;
        }

        if (wr.hasStoredLog) {
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

  openPublishRetryConfirm(): void {
    this._publishRetryErrorSignal.set(null);
    this._confirmPublishRetryOpen.set(true);
  }

  confirmPublishRetry(): void {
    this._confirmPublishRetryOpen.set(false);
    const workerRunId = this.detail()?.stateDetails?.workerRunId ?? this._workerRun()?.workerRunId;

    if (!workerRunId) {
      return;
    }

    this._publishRetryPendingSignal.set(true);
    this._publishRetrySuccessSignal.set(null);
    this._workerRunService.requestPublishRetry(workerRunId).subscribe((error) => {
      this._publishRetryPendingSignal.set(false);
      if (error !== null) {
        this._publishRetryErrorSignal.set(error);
        return;
      }
      this._publishRetrySuccessSignal.set('Publish retry started. Issue status is updating.');
      this._focusContentWhenPublishRetryGone = true;
      // The IssueUpdated reload may already have moved the issue on.
      const current = this.detail();
      if (current !== null) {
        this._focusContentIfPublishRetryGone(current.state);
      }
    });
  }

  private _focusContentIfPublishRetryGone(state: string): void {
    if (!this._focusContentWhenPublishRetryGone || state === 'publish_failed') {
      return;
    }
    this._focusContentWhenPublishRetryGone = false;
    afterNextRender(() => this._contentRegion()?.nativeElement.focus(), { injector: this._injector });
  }

  protected _publishRetryMessage(branchName: string | null): string {
    const target = branchName ?? 'the issue branch';
    return `Foundry pushes the run's commits to ${target} and opens or updates the pull request. CI runs on the push.`;
  }

  protected _isPublishRunState(state: WorkerRunState): boolean {
    return PUBLISH_RUN_STATES.has(state);
  }

  retryIssue(id: string): void {
    this._issueService.retryIssue(id);
  }

  confirmRetryFresh(id: string): void {
    this._confirmRetryFreshOpen.set(false);
    this._issueService.retryIssueFresh(id);
  }

  retryTriage(id: string): void {
    this._issueService.retryTriage(id);
  }

  confirmSkipTriage(id: string): void {
    this._confirmSkipTriageOpen.set(false);
    this._issueService.skipTriage(id);
  }

  confirmRetriage(id: string): void {
    this._confirmRetriageOpen.set(false);
    this._issueService.retriage(id);
  }

  protected _isRetriageable(state: string): boolean {
    return RETRIAGEABLE_STATES.has(state as IssueState);
  }

  protected _retriageMessage(state: string): string {
    return state === 'blocked' ? RETRIAGE_MESSAGE + RETRIAGE_BLOCKED_SUFFIX : RETRIAGE_MESSAGE;
  }

  /** Display label for an issue kind; an unknown value shows as is. */
  protected _kindLabel(kind: string | null): string {
    if (kind === null) {
      return 'Unclassified';
    }
    return KIND_LABELS[kind] ?? kind;
  }

  /** The kind as it reads mid-sentence, e.g. "a refactor" or "documentation". */
  protected _kindPhrase(kind: string): string {
    const lower = (KIND_LABELS[kind] ?? kind).toLowerCase();
    return kind === 'documentation' ? lower : `a ${lower}`;
  }

  protected _kindSourceNote(source: IssueKindClassification['source']): string {
    return KIND_SOURCE_NOTES[source] ?? '';
  }

  protected _questionsAsked(count: number): string {
    return count === 1 ? '1 question asked' : `${count} questions asked`;
  }

  /**
   * Which triage callout row (design §2b) applies to the issue, or null when it is empty. The
   * triage states are checked before the verdict: during a re-triage the latest verdict predates
   * the run and is stale, so it stays hidden.
   */
  protected _triageCallout(d: IssueDetail): TriageCallout | null {
    if (TRIAGE_WAITING_STATES.has(d.state)) {
      return { kind: 'waiting', tint: 'neutral' };
    }
    if (d.state === 'triage_in_progress') {
      return { kind: 'running', tint: 'neutral' };
    }
    if (d.state === 'triage_failed') {
      return { kind: 'failed', tint: 'failed' };
    }
    if (d.state === 'awaiting_input' && d.stateDetails?.awaitingInput?.resumeTarget === 'retriage') {
      return { kind: 'needs_detail', tint: 'warning' };
    }
    if (d.triage) {
      return { kind: 'verdict', tint: 'neutral' };
    }
    if (d.state === 'queued' || d.state === 'blocked') {
      return { kind: 'not_triaged', tint: 'neutral' };
    }
    return null;
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

  protected _failureCategoryLabel(category: string): string {
    return getFailureCategoryDisplay(category)?.label ?? category.toUpperCase().replace(/_/g, ' ');
  }

  protected _failureChipClass(category: string): string {
    return getFailureCategoryDisplay(category)?.cssClass ?? 'badge--failed';
  }

}
