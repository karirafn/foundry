import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InputSignal,
  Injector,
  OutputEmitterRef,
  Signal,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { IssueDetail, IssueSummary } from '../issues/issue.model';
import { IssueService } from '../issues/issue.service';
import { WorkerRunDetail } from '../workers/worker-run.model';
import { WorkerRunService } from '../workers/worker-run.service';
import { WORKING_STATES } from '../../shared/utils/issue-state';
import { PaneActivityComponent } from './pane-activity/pane-activity';
import { PaneFactsComponent } from './pane-facts/pane-facts';
import { PaneHeaderComponent } from './pane-header/pane-header';
import { PaneStatusComponent } from './pane-status/pane-status';

export const ISSUE_PANE_TITLE_ID = 'issue-pane-title';

/**
 * The issue pane's content, rendered inside `fd-side-sheet`: the header, the status line with its
 * one callout and actions, the facts, and, while a worker runs, its activity. It loads the worker
 * run of the selected issue for the layers below it.
 */
@Component({
  selector: 'fd-issue-pane',
  imports: [PaneHeaderComponent, PaneStatusComponent, PaneFactsComponent, PaneActivityComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './issue-pane.html',
  styleUrl: './issue-pane.scss',
})
export class IssuePaneComponent {
  readonly issueId: InputSignal<string> = input.required<string>();
  /** The summary the page already holds; null on a deep link the page holds no summary for. */
  readonly summary: InputSignal<IssueSummary | null> = input<IssueSummary | null>(null);
  readonly closed: OutputEmitterRef<void> = output<void>();

  protected readonly issueService = inject(IssueService);
  private readonly _workerRunService = inject(WorkerRunService);
  protected readonly titleId = ISSUE_PANE_TITLE_ID;
  private readonly _host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly _injector = inject(Injector);

  /** The loaded detail of this pane's issue, or null while it loads. */
  protected readonly detail: Signal<IssueDetail | null> = computed(() => {
    const detail = this.issueService.issueDetail();
    return detail?.id === this.issueId() ? detail : null;
  });

  /** The issue's summary: the live one on the page, else the one the page passed in. */
  protected readonly liveSummary: Signal<IssueSummary | null> = computed(() => {
    const id = this.issueId();
    const live = this.issueService.issues().find((i) => i.id === id);
    if (live !== undefined) {
      return live;
    }
    const summary = this.summary();
    return summary?.id === id ? summary : null;
  });

  /** What the header names: the summary, else the loaded detail (whose fields include a summary's). */
  protected readonly heading: Signal<IssueSummary | null> = computed(() => this.liveSummary() ?? this.detail());

  protected readonly queuePosition: Signal<number | null> = computed(() => {
    const index = this.issueService.eligibleQueuedIssues().findIndex((i) => i.id === this.issueId());
    return index < 0 ? null : index + 1;
  });

  private readonly _run: WritableSignal<WorkerRunDetail | null> = signal(null);
  protected readonly run: Signal<WorkerRunDetail | null> = this._run.asReadonly();

  protected readonly pullRequestUrl: Signal<string | null> = computed(
    () => this.liveSummary()?.pullRequestUrl ?? this.run()?.publish?.pullRequestUrl ?? null);

  /** Activity shows only while a worker runs: a working state that is not publishing. */
  protected readonly showsActivity: Signal<boolean> = computed(() => {
    const detail = this.detail();
    if (detail === null || !WORKING_STATES.has(detail.state)) {
      return false;
    }
    return (this.liveSummary()?.publishPhase ?? detail.publishPhase ?? null) === null;
  });

  protected readonly untracked: Signal<boolean> = computed(
    () => this.issueService.selectedIssueId() === this.issueId() && this.issueService.detailMissing(),
  );

  constructor() {
    // The sheet focuses the heading on open; a swap to another issue does it here.
    let previous: string | null = null;
    effect(() => {
      const id = this.issueId();
      if (previous !== null && previous !== id) {
        untracked(() => this._focusHeading());
      }
      previous = id;
    });

    // Each detail load refreshes the run, so its state follows the issue's.
    let runSub: Subscription | null = null;
    effect(() => {
      const detail = this.detail();
      const workerRunId = detail?.stateDetails?.workerRunId ?? null;
      runSub?.unsubscribe();
      runSub = null;
      if (workerRunId === null) {
        this._run.set(null);
        return;
      }
      if (untracked(() => this._run())?.workerRunId !== workerRunId) {
        this._run.set(null);
      }
      runSub = this._workerRunService.getDetail(workerRunId).subscribe((run) => this._run.set(run));
    });
    inject(DestroyRef).onDestroy(() => runSub?.unsubscribe());
  }

  protected retry(): void {
    this.issueService.loadDetail(this.issueId());
  }

  private _focusHeading(): void {
    afterNextRender(
      () => this._host.querySelector<HTMLElement>(`#${ISSUE_PANE_TITLE_ID}`)?.focus(),
      { injector: this._injector },
    );
  }
}
