import { Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { Observable, of, switchMap } from 'rxjs';
import { ConfirmDialogService } from '../../shared/components/confirm-dialog/confirm-dialog.service';
import { WorkerRunService } from '../workers/worker-run.service';
import { actionConfirmation, actionSuccessMessage } from './issue-action-copy';
import { IssueService } from './issue.service';

/** An operator action on one issue, with the facts its call and its confirmation copy need. */
export type IssueActionRequest =
  | { readonly kind: 'retry_issue' }
  | { readonly kind: 'retry_fresh' }
  | { readonly kind: 'retry_triage' }
  | { readonly kind: 'skip_triage'; readonly issueNumber: number }
  | { readonly kind: 'retriage'; readonly issueNumber: number; readonly blocked: boolean }
  | { readonly kind: 'continue_without_answer'; readonly issueNumber: number; readonly resumesTriage: boolean }
  | { readonly kind: 'retry_post' }
  | { readonly kind: 'retry_review' }
  | { readonly kind: 'stop_worker'; readonly workerRunId: string; readonly triageRun: boolean }
  // A null run id is read from the issue's detail once confirmed: a board row holds no run.
  | { readonly kind: 'retry_publish'; readonly workerRunId: string | null; readonly branchName: string | null }
  | { readonly kind: 'discard_run'; readonly workerRunId: string; readonly providerType: string }
  | { readonly kind: 'confirm_false_positives'; readonly workerRunId: string; readonly publishBlockedAt: string };

export type IssueActionKind = IssueActionRequest['kind'];

/** An action that succeeded, with the announcement for it; null for an action that announces nothing. */
export interface IssueActionSuccess {
  readonly action: IssueActionRequest;
  readonly message: string | null;
}

export interface IssueActionFailure {
  readonly action: IssueActionKind;
  readonly message: string;
}

interface IssueActionState {
  readonly pending: IssueActionKind | null;
  readonly failure: IssueActionFailure | null;
  readonly success: IssueActionSuccess | null;
}

interface IssueActionEntry {
  readonly state: WritableSignal<IssueActionState>;
  readonly pending: Signal<boolean>;
  readonly pendingAction: Signal<IssueActionKind | null>;
  readonly error: Signal<string | null>;
  readonly failure: Signal<IssueActionFailure | null>;
  readonly success: Signal<IssueActionSuccess | null>;
}

// The messages WorkerRunService.requestPublishRetry gives for the same outcomes.
const PUBLISH_RETRY_ERROR = 'Failed to retry publish.';
const PUBLISH_RETRY_GONE = 'This run is no longer waiting to publish.';

const IDLE: IssueActionState = { pending: null, failure: null, success: null };

/**
 * Runs the operator actions on an issue: confirms the ones that need it, calls the endpoint, and
 * keeps the pending action, the failure and the last success per issue, so an action on one issue
 * never shows busy on another, and the state outlives the view that started it.
 */
@Injectable({ providedIn: 'root' })
export class IssueActionsService {
  private readonly _issueService = inject(IssueService);
  private readonly _workerRunService = inject(WorkerRunService);
  private readonly _confirmDialog = inject(ConfirmDialogService);
  private readonly _entries = new Map<string, IssueActionEntry>();

  pending(issueId: string): Signal<boolean> {
    return this._entry(issueId).pending;
  }

  /** The action in flight on the issue, for the label of its button. */
  pendingAction(issueId: string): Signal<IssueActionKind | null> {
    return this._entry(issueId).pendingAction;
  }

  error(issueId: string): Signal<string | null> {
    return this._entry(issueId).error;
  }

  /** The error with the action that failed, for a view that shows only the errors of what it offers. */
  failure(issueId: string): Signal<IssueActionFailure | null> {
    return this._entry(issueId).failure;
  }

  success(issueId: string): Signal<IssueActionSuccess | null> {
    return this._entry(issueId).success;
  }

  clearError(issueId: string): void {
    this._patch(issueId, { failure: null });
  }

  /**
   * Confirms the action where it needs it, then runs it. Ignored while another action on the
   * issue is pending. `confirm_false_positives` is confirmed by its caller, whose dialog lists the
   * findings, so it runs straight away.
   */
  perform(issueId: string, action: IssueActionRequest): void {
    if (this.pending(issueId)()) {
      return;
    }
    this.clearError(issueId);
    const confirmation = actionConfirmation(action);
    if (confirmation === null) {
      this._run(issueId, action);
      return;
    }
    void this._confirmDialog.confirm(confirmation).then((confirmed) => {
      if (confirmed && !this.pending(issueId)()) {
        this._run(issueId, action);
      }
    });
  }

  private _run(issueId: string, action: IssueActionRequest): void {
    this._patch(issueId, { pending: action.kind, failure: null, success: null });
    this._call(issueId, action).subscribe((error) => {
      if (error !== null) {
        this._patch(issueId, { pending: null, failure: { action: action.kind, message: error } });
        return;
      }
      this._patch(issueId, { pending: null, success: { action, message: actionSuccessMessage(action.kind) } });
    });
  }

  private _call(issueId: string, action: IssueActionRequest): Observable<string | null> {
    switch (action.kind) {
      case 'retry_issue':
        return this._issueService.retryIssue(issueId);
      case 'retry_fresh':
        return this._issueService.retryIssueFresh(issueId);
      case 'retry_triage':
        return this._issueService.retryTriage(issueId);
      case 'skip_triage':
        return this._issueService.skipTriage(issueId);
      case 'retriage':
        return this._issueService.retriage(issueId);
      case 'continue_without_answer':
        return this._issueService.continueWithoutAnswer(issueId);
      case 'retry_post':
        return this._issueService.retryQuestionPost(issueId);
      case 'retry_review':
        return this._issueService.retryReview(issueId);
      case 'stop_worker':
        return this._workerRunService.requestCancel(action.workerRunId);
      case 'retry_publish':
        return action.workerRunId === null
          ? this._retryPublishOfDetail(issueId)
          : this._workerRunService.requestPublishRetry(action.workerRunId);
      case 'discard_run':
        return this._workerRunService.requestDiscard(action.workerRunId);
      case 'confirm_false_positives':
        return this._workerRunService.requestConfirmFalsePositives(action.workerRunId, action.publishBlockedAt);
    }
  }

  private _retryPublishOfDetail(issueId: string): Observable<string | null> {
    return this._issueService.fetchDetail(issueId).pipe(
      switchMap((detail) => {
        if (detail === null) {
          return of(PUBLISH_RETRY_ERROR);
        }
        const workerRunId = detail.stateDetails?.workerRunId;
        return workerRunId
          ? this._workerRunService.requestPublishRetry(workerRunId)
          : of(PUBLISH_RETRY_GONE);
      }),
    );
  }

  private _patch(issueId: string, change: Partial<IssueActionState>): void {
    this._entry(issueId).state.update((current) => ({ ...current, ...change }));
  }

  private _entry(issueId: string): IssueActionEntry {
    const existing = this._entries.get(issueId);
    if (existing !== undefined) {
      return existing;
    }
    const state = signal<IssueActionState>(IDLE);
    const entry: IssueActionEntry = {
      state,
      pending: computed(() => state().pending !== null),
      pendingAction: computed(() => state().pending),
      error: computed(() => state().failure?.message ?? null),
      failure: computed(() => state().failure),
      success: computed(() => state().success),
    };
    this._entries.set(issueId, entry);
    return entry;
  }
}
