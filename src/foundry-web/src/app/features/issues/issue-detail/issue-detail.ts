import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Observable } from 'rxjs';
import {
  IssueDetail,
  IssueKindClassification,
  IssueStateDetails,
  TriageVerdictDetails,
} from '../issue.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { WorkerSignalRService } from '../../../core/services/worker-signalr.service';
import { PUBLISH_RUN_STATES, WorkerRunDetail, WorkerRunState } from '../../workers/worker-run.model';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { PublishFactsComponent } from '../publish-facts/publish-facts';
import { RevertedFilesComponent } from '../reverted-files/reverted-files';
import { rebaseOutcomeLabel } from '../rebase-outcome.format';

let nextBlockersLabelId = 0;

const KIND_LABELS: Readonly<Record<string, string>> = {
  feature: 'Feature',
  bug: 'Bug',
  refactor: 'Refactor',
  documentation: 'Documentation',
};

const TRIAGE_OUTCOME_LABELS = {
  ready: 'Ready',
  needs_input: 'Needs input',
  blocked: 'Blocked',
} as const satisfies Record<TriageVerdictDetails['outcome'], string>;

const KIND_SOURCE_NOTES: Readonly<Record<IssueKindClassification['source'], string>> = {
  labelled: 'From label',
  triaged: 'From triage',
  unclassified: 'No kind label, and triage has not classified it',
};

// States in which the triage verdict is stale or not yet decided: the pane's callout tells the
// triage story instead (#862).
const TRIAGE_CALLOUT_STATES: ReadonlySet<string> = new Set<string>([
  'detected',
  'triage_queued',
  'triage_blocked',
  'triage_in_progress',
  'triage_failed',
]);

type TriageFact = 'verdict' | 'not_triaged';

/**
 * The facts and logs of an issue, below the pane's header and status (#862). Callouts and actions
 * live in the pane; #863 replaces what is left here.
 */
@Component({
  selector: 'fd-issue-detail',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    LogViewComponent,
    RoleTagComponent,
    PublishFactsComponent,
    RevertedFilesComponent,
  ],
  template: `
    @let d = detail();
      <div
        class="issue-detail__content"
        role="region"
        [attr.aria-label]="'Issue details for #' + d.issueNumber"
      >
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

            @if (s.blockedBy?.length) {
              <div class="issue-detail__field issue-detail__field--full-width">
                <span class="issue-detail__field-key" [id]="_blockersLabelId">Blocked By</span>
                <ul class="issue-detail__blockers" role="list" [attr.aria-labelledby]="_blockersLabelId">
                  @for (b of s.blockedBy; track b.issueNumber) {
                    <li class="issue-detail__blocker">
                      <span class="issue-detail__field-value issue-detail__blocker-number">#{{ b.issueNumber }}</span>
                      @if (b.reason) {
                        <span class="issue-detail__blocker-reason">Triage: {{ b.reason }}</span>
                      }
                    </li>
                  }
                </ul>
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

        @if (_triageFact(d); as fact) {
          <div class="issue-detail__fields issue-detail__fields--triage">
            <div class="issue-detail__field issue-detail__field--full-width issue-detail__triage-fact">
              <span class="issue-detail__field-key">Triage</span>
              <span class="issue-detail__kind-line">
                @if (fact === 'verdict' && d.triage; as t) {
                  <span
                    class="badge issue-detail__chip"
                    [class.issue-detail__chip--success]="t.outcome === 'ready'"
                    [class.issue-detail__chip--warning]="t.outcome === 'needs_input'"
                    [class.issue-detail__chip--neutral]="t.outcome === 'blocked'"
                  >{{ _triageOutcomeLabel(t.outcome) }}</span>
                  <span class="issue-detail__field-value">{{ _kindLabel(t.kind) }}</span>
                  <span class="issue-detail__field-value issue-detail__triage-message">Triaged {{ t.decidedAt | date: 'medium' }}</span>
                  @if (t.outcome === 'blocked') {
                    <span class="issue-detail__field-value issue-detail__triage-message">Triage found open issues this depends on.</span>
                  }
                  @if (t.outcome === 'needs_input') {
                    <span class="issue-detail__field-value issue-detail__triage-message">{{ _questionsAsked(t.questions.length) }}</span>
                  }
                } @else {
                  <span class="issue-detail__field-value issue-detail__triage-message"
                    >Not triaged. This issue was queued without a readiness check.</span>
                }
              </span>
            </div>
          </div>
        }

        <div class="issue-detail__fields issue-detail__fields--author">
          <div class="issue-detail__field issue-detail__field--full-width">
            <span class="issue-detail__field-key">Author</span>
            <span class="issue-detail__field-value">{{ d.author }}</span>
          </div>
        </div>

        @if (run(); as wr) {
          <div class="issue-detail__worker-run">
            @if (wr.publish; as published) {
              <fd-publish-facts
                [facts]="published"
                [branchName]="d.stateDetails?.branchName ?? null"
                [providerType]="d.providerType ?? ''"
              />
            }
            @if (wr.revertedFiles.length > 0) {
              <fd-reverted-files [files]="wr.revertedFiles" />
            }
            <div class="issue-detail__run-header">
              <span class="issue-detail__field-key">Run</span>
              <fd-role-tag [role]="wr.role" />
            </div>

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
      </div>
  `,
  styleUrl: './issue-detail.scss',
})
export class IssueDetailComponent {
  readonly detail: InputSignal<IssueDetail> = input.required<IssueDetail>();
  /** The issue's worker run, loaded by the pane; null when it has none or it has not loaded. */
  readonly run: InputSignal<WorkerRunDetail | null> = input<WorkerRunDetail | null>(null);

  // The side pane can render two details at once, so the id cannot be a fixed string.
  protected readonly _blockersLabelId = `issue-detail-blockers-label-${nextBlockersLabelId++}`;

  private readonly _workerRunService = inject(WorkerRunService);
  private readonly _workerSignalR = inject(WorkerSignalRService);

  private readonly _staticLogLinesSignal: WritableSignal<string[] | null> = signal(null);
  protected readonly _staticLogLines: Signal<string[] | null> = this._staticLogLinesSignal.asReadonly();

  private readonly _liveLogStreamSignal: WritableSignal<Observable<string> | null> = signal(null);
  protected readonly _liveLogStream: Signal<Observable<string> | null> = this._liveLogStreamSignal.asReadonly();

  constructor() {
    // The log follows the run: fetched once it is stored, streamed while it runs. A reload of the
    // same run in the same state keeps what is already shown.
    let loadedFor: string | null = null;
    effect(() => {
      const wr = this.run();
      const key = wr === null ? null : `${wr.workerRunId}:${wr.state}:${wr.hasStoredLog}`;
      if (key === loadedFor) {
        return;
      }
      loadedFor = key;
      untracked(() => {
        this._staticLogLinesSignal.set(null);
        this._liveLogStreamSignal.set(null);
        // Publish states show no log view, so there is no log to fetch or stream.
        if (wr === null || PUBLISH_RUN_STATES.has(wr.state)) {
          return;
        }
        if (wr.hasStoredLog) {
          this._workerRunService.getLog(wr.workerRunId).subscribe((text) => {
            this._staticLogLinesSignal.set(text ? text.split('\n') : []);
          });
        } else if (wr.state === 'running') {
          this._liveLogStreamSignal.set(this._workerSignalR.streamLog(wr.workerRunId));
        }
      });
    });
  }

  protected _isPublishRunState(state: WorkerRunState): boolean {
    return PUBLISH_RUN_STATES.has(state);
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
   * The triage fact row: the latest verdict, or that the issue was queued without one. Hidden
   * while the callout tells the triage story, because a verdict then predates the run.
   */
  protected _triageFact(d: IssueDetail): TriageFact | null {
    if (TRIAGE_CALLOUT_STATES.has(d.state)) {
      return null;
    }
    if (d.state === 'awaiting_input' && d.stateDetails?.awaitingInput?.resumeTarget === 'retriage') {
      return null;
    }
    if (d.triage) {
      return 'verdict';
    }
    if (d.state === 'queued' || d.state === 'blocked') {
      return 'not_triaged';
    }
    return null;
  }

  protected _rebaseOutcomeLabel(s: IssueStateDetails): string | null {
    return rebaseOutcomeLabel(s.rebaseOutcome, s.rebaseSkippedReason);
  }

  // The outcome arrives as a server string, so an outcome added later renders as itself.
  protected _triageOutcomeLabel(outcome: string): string {
    return Object.prototype.hasOwnProperty.call(TRIAGE_OUTCOME_LABELS, outcome)
      ? TRIAGE_OUTCOME_LABELS[outcome as TriageVerdictDetails['outcome']]
      : outcome;
  }
}
