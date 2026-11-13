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
  untracked,
} from '@angular/core';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { providerDisplayName } from '../../../shared/utils/provider.util';
import { IssueDetail } from '../../issues/issue.model';
import { IssueService, TriageAction } from '../../issues/issue.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { WorkerRunService } from '../../workers/worker-run.service';
import { PaneAction } from '../pane-callout.model';

const RETRIAGE_MESSAGE =
  'The issue leaves the queue and a triage worker reads it again. If it needs more detail, the questions are posted on the issue and it waits for a reply.';
const RETRIAGE_BLOCKED_SUFFIX = ' While its blockers are open it waits untriaged, and triage runs once they close.';

// Actions whose outcome IssueService reports through its retry signals.
const RETRY_ACTIONS: ReadonlySet<PaneAction> = new Set<PaneAction>(['retry_issue', 'retry_fresh']);
const TRIAGE_ACTIONS: ReadonlySet<PaneAction> = new Set<PaneAction>(['retry_triage', 'skip_triage', 'retriage']);

let nextId = 0;

/**
 * The buttons for the callout's actions, with their confirm dialogs and their pending, error and
 * success signals. The success announcements stay mounted outside the button row, so they survive
 * the reload that removes the clicked button. `succeeded` names the action once it succeeds.
 */
@Component({
  selector: 'fd-pane-actions',
  imports: [ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pane-actions.html',
  styleUrl: './pane-actions.scss',
})
export class PaneActionsComponent {
  readonly detail: InputSignal<IssueDetail> = input.required<IssueDetail>();
  readonly run: InputSignal<WorkerRunDetail | null> = input.required<WorkerRunDetail | null>();
  readonly actions: InputSignal<readonly PaneAction[]> = input.required<readonly PaneAction[]>();
  /** The provider of the issue, for the copy that names it; empty when unknown. */
  readonly providerType: InputSignal<string> = input<string>('');
  readonly succeeded: OutputEmitterRef<PaneAction> = output<PaneAction>();

  protected readonly issueService = inject(IssueService);
  private readonly _workerRunService = inject(WorkerRunService);

  protected readonly hintId = `pane-actions-hint-${nextId++}`;

  // On SIGTERM the worker bundles its work and the Publisher pushes it.
  protected readonly implementCancelMessage = "Foundry publishes the run's commits to the branch; Retry continues from them.";

  protected readonly confirmCancelOpen: WritableSignal<boolean> = signal(false);
  protected readonly confirmRetryFreshOpen: WritableSignal<boolean> = signal(false);
  protected readonly confirmSkipTriageOpen: WritableSignal<boolean> = signal(false);
  protected readonly confirmRetriageOpen: WritableSignal<boolean> = signal(false);
  protected readonly confirmPublishRetryOpen: WritableSignal<boolean> = signal(false);
  protected readonly confirmDiscardOpen: WritableSignal<boolean> = signal(false);

  // Set once the user confirmed a stop in this view; reset whenever the detail reloads, after
  // which the persisted cancelRequestedAt takes over.
  private readonly _cancelSubmitted: WritableSignal<boolean> = signal(false);
  private readonly _cancelError: WritableSignal<string | null> = signal(null);

  private readonly _publishRetryPending: WritableSignal<boolean> = signal(false);
  protected readonly publishRetryPending: Signal<boolean> = this._publishRetryPending.asReadonly();
  private readonly _publishRetryError: WritableSignal<string | null> = signal(null);
  // Not reset by a detail reload, so it survives the IssueUpdated reload it causes.
  private readonly _publishRetrySuccess: WritableSignal<string | null> = signal(null);
  protected readonly publishRetrySuccess: Signal<string | null> = this._publishRetrySuccess.asReadonly();

  private readonly _discardPending: WritableSignal<boolean> = signal(false);
  protected readonly discardPending: Signal<boolean> = this._discardPending.asReadonly();
  private readonly _discardError: WritableSignal<string | null> = signal(null);
  private readonly _discardSuccess: WritableSignal<string | null> = signal(null);
  protected readonly discardSuccess: Signal<string | null> = this._discardSuccess.asReadonly();

  protected readonly triagePending: Signal<TriageAction | null> = computed(() =>
    this.issueService.triageActionPending());

  protected readonly cancelling: Signal<boolean> = computed(() => {
    const run = this.run();
    return this._cancelSubmitted() || (run?.state === 'running' && run.cancelRequestedAt !== null);
  });

  protected readonly canCancel: Signal<boolean> = computed(() => this.run()?.state === 'running' && !this.cancelling());

  protected readonly cancelHint: Signal<string | null> = computed(() => {
    if (this.cancelling()) {
      return null;
    }
    const run = this.run();
    return run === null || run.state === 'starting' ? 'Starting…' : null;
  });

  /** The one error to show: this view's own first, then the service's for the offered actions. */
  protected readonly error: Signal<string | null> = computed(() => {
    const local = this._cancelError() ?? this._publishRetryError() ?? this._discardError();
    if (local !== null) {
      return local;
    }
    const actions = this.actions();
    if (actions.some((a) => RETRY_ACTIONS.has(a)) && this.issueService.retryError() !== null) {
      return this.issueService.retryError();
    }
    if (actions.some((a) => TRIAGE_ACTIONS.has(a))) {
      return this.issueService.triageActionError();
    }
    return null;
  });

  protected readonly retriageMessage: Signal<string> = computed(() =>
    this.detail().state === 'blocked' ? RETRIAGE_MESSAGE + RETRIAGE_BLOCKED_SUFFIX : RETRIAGE_MESSAGE);

  protected readonly publishRetryMessage: Signal<string> = computed(() => {
    const target = this.detail().stateDetails?.branchName ?? 'the issue branch';
    return `Foundry pushes the run's commits to ${target} and opens or updates the pull request. CI runs on the push.`;
  });

  protected readonly discardMessage: Signal<string> = computed(() =>
    "The run's commits are deleted from this host and nothing is pushed. The issue moves to Failed, where "
      + `Retry issue starts a new worker from the branch on ${providerDisplayName(this.providerType()) || 'the provider'}. This cannot be undone.`);

  // The service-backed action the user last started here, named when the service reports success.
  private _serviceAction: PaneAction | null = null;

  constructor() {
    effect(() => {
      this.detail();
      untracked(() => {
        this._cancelSubmitted.set(false);
        this._cancelError.set(null);
        this.confirmCancelOpen.set(false);
        this.confirmRetryFreshOpen.set(false);
        this.confirmSkipTriageOpen.set(false);
        this.confirmRetriageOpen.set(false);
        this.confirmPublishRetryOpen.set(false);
        this._publishRetryError.set(null);
        this.confirmDiscardOpen.set(false);
        this._discardError.set(null);
      });
    });

    effect(() => {
      const retrySuccess = this.issueService.retrySuccess();
      const triageSuccess = this.issueService.triageActionSuccess();
      const action = untracked(() => this._serviceAction);
      if (action === null) {
        return;
      }
      // Only the signal of the started action counts; the other may still hold an earlier success.
      const success = RETRY_ACTIONS.has(action) ? retrySuccess : triageSuccess;
      if (success !== null) {
        this._serviceAction = null;
        this.succeeded.emit(action);
      }
    });
  }

  protected offers(action: PaneAction): boolean {
    return this.actions().includes(action);
  }

  protected buttonClass(primary: boolean): string {
    return primary ? 'pane-actions__btn pane-actions__btn--primary' : 'pane-actions__btn';
  }

  protected openCancelConfirm(): void {
    this._cancelError.set(null);
    this.confirmCancelOpen.set(true);
  }

  protected confirmCancel(): void {
    this.confirmCancelOpen.set(false);
    const workerRunId = this.run()?.workerRunId;
    if (!workerRunId) {
      return;
    }
    this._cancelSubmitted.set(true);
    this._workerRunService.requestCancel(workerRunId).subscribe((error) => {
      if (error !== null) {
        this._cancelSubmitted.set(false);
        this._cancelError.set(error);
      }
    });
  }

  protected openPublishRetryConfirm(): void {
    this._publishRetryError.set(null);
    this._discardError.set(null);
    this.confirmPublishRetryOpen.set(true);
  }

  protected confirmPublishRetry(): void {
    this.confirmPublishRetryOpen.set(false);
    const workerRunId = this._workerRunId();
    if (!workerRunId) {
      return;
    }
    this._publishRetryPending.set(true);
    this._publishRetrySuccess.set(null);
    this._workerRunService.requestPublishRetry(workerRunId).subscribe((error) => {
      this._publishRetryPending.set(false);
      if (error !== null) {
        this._publishRetryError.set(error);
        return;
      }
      this._publishRetrySuccess.set('Publish retry started. Issue status is updating.');
      this.succeeded.emit('retry_publish');
    });
  }

  protected openDiscardConfirm(): void {
    this._discardError.set(null);
    this._publishRetryError.set(null);
    this.confirmDiscardOpen.set(true);
  }

  protected confirmDiscard(): void {
    this.confirmDiscardOpen.set(false);
    const workerRunId = this._workerRunId();
    if (!workerRunId) {
      return;
    }
    this._discardPending.set(true);
    this._discardSuccess.set(null);
    this._workerRunService.requestDiscard(workerRunId).subscribe((error) => {
      this._discardPending.set(false);
      if (error !== null) {
        this._discardError.set(error);
        return;
      }
      this._discardSuccess.set('Run discarded. Issue status is updating.');
      this.succeeded.emit('discard_run');
    });
  }

  protected retryIssue(): void {
    this._serviceAction = 'retry_issue';
    this.issueService.retryIssue(this.detail().id);
  }

  protected confirmRetryFresh(): void {
    this.confirmRetryFreshOpen.set(false);
    this._serviceAction = 'retry_fresh';
    this.issueService.retryIssueFresh(this.detail().id);
  }

  protected retryTriage(): void {
    this._serviceAction = 'retry_triage';
    this.issueService.retryTriage(this.detail().id);
  }

  protected confirmSkipTriage(): void {
    this.confirmSkipTriageOpen.set(false);
    this._serviceAction = 'skip_triage';
    this.issueService.skipTriage(this.detail().id);
  }

  protected confirmRetriage(): void {
    this.confirmRetriageOpen.set(false);
    this._serviceAction = 'retriage';
    this.issueService.retriage(this.detail().id);
  }

  private _workerRunId(): string | null {
    return this.detail().stateDetails?.workerRunId ?? this.run()?.workerRunId ?? null;
  }
}
