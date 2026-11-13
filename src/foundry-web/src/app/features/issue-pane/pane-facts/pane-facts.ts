import {
  ChangeDetectionStrategy,
  Component,
  InputSignal,
  Signal,
  WritableSignal,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { IssueDetail, IssueSummary } from '../../issues/issue.model';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { LogViewComponent } from '../../../shared/components/log-view/log-view';
import { RoleTagComponent } from '../../../shared/components/role-tag/role-tag';
import { SafeHrefPipe } from '../../../shared/pipes/safe-href.pipe';
import { RevertedFilesComponent } from '../../issues/reverted-files/reverted-files';
import { PaneFact, buildFacts } from './pane-facts.model';

let nextId = 0;

/** The pane's Details: one `dt`/`dd` pair per fact, empty facts left out (#863). */
@Component({
  selector: 'fd-pane-facts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, LogViewComponent, RoleTagComponent, RevertedFilesComponent, SafeHrefPipe],
  templateUrl: './pane-facts.html',
  styleUrl: './pane-facts.scss',
})
export class PaneFactsComponent {
  /** The loaded detail; null while it loads. */
  readonly detail: InputSignal<IssueDetail | null> = input.required<IssueDetail | null>();
  readonly run: InputSignal<WorkerRunDetail | null> = input<WorkerRunDetail | null>(null);
  readonly summary: InputSignal<IssueSummary | null> = input<IssueSummary | null>(null);

  // The pane can render two issues at once while it swaps, so ids cannot be fixed strings.
  private readonly _id = nextId++;
  protected readonly headingId = `pane-facts-heading-${this._id}`;
  protected readonly blockersKeyId = `pane-facts-blockers-${this._id}`;
  protected readonly resultId = `pane-facts-result-${this._id}`;

  private readonly _workerRunService = inject(WorkerRunService);

  protected readonly facts: Signal<readonly PaneFact[]> = computed(() => {
    const detail = this.detail();
    return detail === null ? [] : buildFacts(detail, this.run(), this.summary());
  });

  private readonly _runId: Signal<string | null> = computed(() => this.run()?.workerRunId ?? null);

  private readonly _logRunId: Signal<string | null> = computed(() => {
    const log = this.facts().find((f) => f.type === 'log');
    return log?.type === 'log' ? log.workerRunId : null;
  });

  private readonly _logLines: WritableSignal<string[] | null> = signal(null);
  protected readonly logLines: Signal<string[] | null> = this._logLines.asReadonly();

  private readonly _resultExpanded: WritableSignal<boolean> = signal(false);
  protected readonly resultExpanded: Signal<boolean> = this._resultExpanded.asReadonly();

  constructor() {
    // The stored log is fetched once per run; a reload of the same run keeps what is shown.
    effect(() => {
      const runId = this._logRunId();
      untracked(() => {
        this._logLines.set(null);
        if (runId === null) {
          return;
        }
        this._workerRunService.getLog(runId).subscribe((text) => this._logLines.set(text ? text.split('\n') : []));
      });
    });

    // A long result starts clamped again for every run.
    effect(() => {
      this._runId();
      untracked(() => this._resultExpanded.set(false));
    });
  }

  protected toggleResult(): void {
    this._resultExpanded.update((v) => !v);
  }
}
