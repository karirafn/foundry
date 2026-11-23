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
import { IssueDetail } from '../../issues/issue.model';
import { confirmFalsePositivesCopy } from '../../issues/issue-action-copy';
import { IssueActionKind, IssueActionRequest, IssueActionsService } from '../../issues/issue-actions.service';
import { WorkerRunDetail } from '../../workers/worker-run.model';
import { SecretFindingsComponent } from '../../issues/secret-findings/secret-findings';
import { PaneAction } from '../pane-callout.model';

// The live region each action's success is announced in.
const RETRY_ACTIONS: ReadonlySet<IssueActionKind> = new Set<IssueActionKind>(['retry_issue', 'retry_fresh']);
const ISSUE_ACTIONS: ReadonlySet<IssueActionKind> = new Set<IssueActionKind>([
  'retry_triage',
  'skip_triage',
  'retriage',
  'continue_without_answer',
  'retry_post',
  'retry_review',
]);

let nextId = 0;

/**
 * The buttons for the callout's actions. IssueActionsService runs each action, with its
 * confirmation and its pending, error and success state; only Confirm false positives keeps its
 * dialog here, as its body lists the findings. The success announcements stay mounted outside the
 * button row, so they survive the reload that removes the clicked button. `succeeded` names the
 * action once it succeeds.
 */
@Component({
  selector: 'fd-pane-actions',
  imports: [ConfirmDialogComponent, SecretFindingsComponent],
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

  private readonly _issueActions = inject(IssueActionsService);

  protected readonly hintId = `pane-actions-hint-${nextId++}`;

  protected readonly confirmFalsePositivesOpen: WritableSignal<boolean> = signal(false);
  // Unchecked on every open, so each confirmation takes a fresh look at the list.
  protected readonly falsePositivesAcknowledged: WritableSignal<boolean> = signal(false);

  protected readonly pendingAction: Signal<IssueActionKind | null> = computed(() =>
    this._issueActions.pendingAction(this.detail().id)());

  /** Any action on the issue in flight: each disables the others. */
  protected readonly busy: Signal<boolean> = computed(() => this.pendingAction() !== null);

  private readonly _success = computed(() => this._issueActions.success(this.detail().id)());

  /** The block time of the run as rendered; null until a blocked run has loaded. */
  private readonly _publishBlockedAt: Signal<string | null> = computed(() => this.run()?.publishBlockedAt ?? null);

  protected readonly cancelling: Signal<boolean> = computed(() => {
    const run = this.run();
    if (this.pendingAction() === 'stop_worker') {
      return true;
    }
    if (run?.state !== 'running') {
      return false;
    }
    const success = this._success();
    const stopped = success?.action.kind === 'stop_worker' && success.action.workerRunId === run.workerRunId;
    return stopped || run.cancelRequestedAt !== null;
  });

  protected readonly canCancel: Signal<boolean> = computed(() => this.run()?.state === 'running' && !this.cancelling());

  protected readonly cancelHint: Signal<string | null> = computed(() => {
    if (this.cancelling()) {
      return null;
    }
    const run = this.run();
    return run === null || run.state === 'starting' ? 'Starting…' : null;
  });

  /** The error of an action this view offers; an earlier one's is stale once the issue moved on. */
  protected readonly error: Signal<string | null> = computed(() => {
    const failure = this._issueActions.failure(this.detail().id)();
    return failure !== null && this.actions().includes(failure.action) ? failure.message : null;
  });

  protected readonly retrySuccess: Signal<string | null> = this._successIn((kind) => RETRY_ACTIONS.has(kind));
  protected readonly issueActionSuccess: Signal<string | null> = this._successIn((kind) => ISSUE_ACTIONS.has(kind));
  protected readonly publishRetrySuccess: Signal<string | null> = this._successIn((kind) => kind === 'retry_publish');
  protected readonly discardSuccess: Signal<string | null> = this._successIn((kind) => kind === 'discard_run');
  protected readonly confirmFalsePositivesSuccess: Signal<string | null> =
    this._successIn((kind) => kind === 'confirm_false_positives');

  protected readonly confirmFalsePositivesCopy = computed(() =>
    confirmFalsePositivesCopy(
      this.run()?.secretFindings.length ?? 0,
      this.detail().stateDetails?.branchName ?? null,
      this.providerType(),
    ));

  // The action the user last started here, reported through `succeeded` once the service says so.
  private _started: IssueActionRequest | null = null;

  constructor() {
    effect(() => {
      this.detail();
      untracked(() => this.confirmFalsePositivesOpen.set(false));
    });

    // A run blocked again holds a finding set the open dialog was not opened on.
    effect(() => {
      this._publishBlockedAt();
      untracked(() => this.confirmFalsePositivesOpen.set(false));
    });

    effect(() => {
      const success = this._success();
      if (success !== null && success.action === this._started) {
        this._started = null;
        this.succeeded.emit(success.action.kind);
      }
    });
  }

  protected offers(action: PaneAction): boolean {
    return this.actions().includes(action);
  }

  /**
   * The first action is filled, except Discard run and the confirmation: Foundry cannot know which
   * outcome of a blocked run is right, and a filled button reads as its recommendation.
   */
  protected buttonClass(action: PaneAction, first: boolean): string {
    if (action === 'discard_run') {
      return 'pane-actions__btn pane-actions__btn--destructive';
    }
    if (action === 'confirm_false_positives') {
      return 'pane-actions__btn pane-actions__btn--neutral';
    }
    return first ? 'pane-actions__btn pane-actions__btn--primary' : 'pane-actions__btn';
  }

  protected perform(kind: Exclude<PaneAction, 'confirm_false_positives'>): void {
    const action = this._request(kind);
    if (action === null) {
      return;
    }
    this._started = action;
    this._issueActions.perform(this.detail().id, action);
  }

  protected openConfirmFalsePositives(): void {
    this._issueActions.clearError(this.detail().id);
    this.falsePositivesAcknowledged.set(false);
    this.confirmFalsePositivesOpen.set(true);
  }

  protected confirmFalsePositives(): void {
    this.confirmFalsePositivesOpen.set(false);
    const workerRunId = this._workerRunId();
    const publishBlockedAt = this._publishBlockedAt();
    if (!workerRunId || publishBlockedAt === null || !this.falsePositivesAcknowledged()) {
      return;
    }
    const action: IssueActionRequest = { kind: 'confirm_false_positives', workerRunId, publishBlockedAt };
    this._started = action;
    this._issueActions.perform(this.detail().id, action);
  }

  private _request(kind: Exclude<PaneAction, 'confirm_false_positives'>): IssueActionRequest | null {
    const d = this.detail();
    switch (kind) {
      case 'retry_issue':
      case 'retry_fresh':
      case 'retry_triage':
      case 'retry_post':
      case 'retry_review':
        return { kind };
      case 'skip_triage':
        return { kind, issueNumber: d.issueNumber };
      case 'retriage':
        return { kind, issueNumber: d.issueNumber, blocked: d.state === 'blocked' };
      case 'continue_without_answer':
        return {
          kind,
          issueNumber: d.issueNumber,
          resumesTriage: d.stateDetails?.awaitingInput?.resumeTarget === 'retriage',
        };
      case 'stop_worker': {
        const workerRunId = this.run()?.workerRunId;
        return workerRunId ? { kind, workerRunId, triageRun: d.state === 'triage_in_progress' } : null;
      }
      case 'retry_publish': {
        const workerRunId = this._workerRunId();
        return workerRunId ? { kind, workerRunId, branchName: d.stateDetails?.branchName ?? null } : null;
      }
      case 'discard_run': {
        const workerRunId = this._workerRunId();
        return workerRunId ? { kind, workerRunId, providerType: this.providerType() } : null;
      }
    }
  }

  private _successIn(inGroup: (kind: IssueActionKind) => boolean): Signal<string | null> {
    return computed(() => {
      const success = this._success();
      return success !== null && inGroup(success.action.kind) ? success.message : null;
    });
  }

  private _workerRunId(): string | null {
    return this.detail().stateDetails?.workerRunId ?? this.run()?.workerRunId ?? null;
  }
}
