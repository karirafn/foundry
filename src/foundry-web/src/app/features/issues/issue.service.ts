import { Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueDetail, IssueState, IssueSummary, LIVE_STATES } from './issue.model';

interface IssueCountsResponse {
  counts: Record<string, number>;
}

const LOAD_ISSUES_ERROR = 'Failed to load issues';
const LOAD_DETAIL_ERROR = 'Failed to load issue details';
const RETRY_FAILED_ERROR = 'Failed to retry issue.';
const RETRY_FAILED_SUCCESS = 'Retry queued. Issue status is updating.';
const SAFE_ID_RE = /^[\w-]+$/;

@Injectable({ providedIn: 'root' })
export class IssueService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(IssueSignalRService);

  readonly issues: WritableSignal<IssueSummary[]> = signal([]);
  readonly expandedIssueId: WritableSignal<string | null> = signal(null);
  readonly issueDetail: WritableSignal<IssueDetail | null> = signal(null);
  readonly detailLoading: WritableSignal<boolean> = signal(false);
  readonly initialLoading: WritableSignal<boolean> = signal(true);
  readonly retryingEligibility: WritableSignal<boolean> = signal(false);
  readonly retryingFailed: WritableSignal<boolean> = signal(false);

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _detailErrorSignal: WritableSignal<string | null> = signal(null);
  readonly detailError: Signal<string | null> = this._detailErrorSignal.asReadonly();

  private readonly _retryFailedErrorSignal: WritableSignal<string | null> = signal(null);
  readonly retryFailedError: Signal<string | null> = this._retryFailedErrorSignal.asReadonly();

  private readonly _retryFailedSuccessSignal: WritableSignal<string | null> = signal(null);
  readonly retryFailedSuccess: Signal<string | null> = this._retryFailedSuccessSignal.asReadonly();

  private readonly _countsSignal: WritableSignal<Record<string, number>> = signal({});
  readonly counts: Signal<Record<string, number>> = this._countsSignal.asReadonly();

  private _detailSub: Subscription | null = null;

  readonly sortedIssues: Signal<IssueSummary[]> = computed(() => {
    const byDate = (a: IssueSummary, b: IssueSummary): number =>
      new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime();
    const all = this.issues();
    const live = [...all].filter(i => LIVE_STATES.has(i.state)).sort(byDate);
    const other = [...all].filter(i => !LIVE_STATES.has(i.state)).sort(byDate);
    return [...live, ...other];
  });

  readonly liveIssueCount: Signal<number> = computed(() =>
    this.issues().filter(i => LIVE_STATES.has(i.state)).length
  );

  readonly isEmpty: Signal<boolean> = computed(() => this.issues().length === 0);

  constructor() {
    this._signalR.on<IssueSummary>('IssueUpdated', (updated) => this._upsertIssue(updated));
    this._signalR.onReconnected(() => this.loadIssues());
  }

  loadIssues(repositoryId?: string): void {
    this._loadErrorSignal.set(null);

    let params = new HttpParams();
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<IssueSummary[]>('/api/issues', { params }).subscribe({
      next: (issues) => {
        this.issues.set(issues.filter(i => SAFE_ID_RE.test(i.id)));
        this._loadErrorSignal.set(null);
        this.initialLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this._loadErrorSignal.set(LOAD_ISSUES_ERROR);
        this.initialLoading.set(false);
      },
    });
  }

  loadCounts(repositoryId?: string): void {
    let params = new HttpParams();
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<IssueCountsResponse>('/api/issues/counts', { params }).subscribe({
      next: (response) => {
        this._countsSignal.set(response.counts);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
      },
    });
  }

  countFor(state: IssueState): number {
    return this._countsSignal()[state] ?? 0;
  }

  loadDetail(id: string): void {
    this._detailSub?.unsubscribe();
    this._detailErrorSignal.set(null);
    this.detailLoading.set(true);
    this._detailSub = this._http.get<IssueDetail>(`/api/issues/${encodeURIComponent(id)}`).subscribe({
      next: (detail) => {
        const expanded = this.expandedIssueId();
        if (expanded !== null && expanded !== id) {
          return;
        }
        this.issueDetail.set(detail);
        this.detailLoading.set(false);
        this._detailErrorSignal.set(null);
      },
      error: (err: HttpErrorResponse) => {
        const expanded = this.expandedIssueId();
        if (expanded !== null && expanded !== id) {
          return;
        }
        console.error(err);
        this.detailLoading.set(false);
        this._detailErrorSignal.set(LOAD_DETAIL_ERROR);
      },
    });
  }

  toggleExpand(id: string): void {
    this._retryFailedErrorSignal.set(null);
    this._retryFailedSuccessSignal.set(null);
    this.retryingFailed.set(false);

    if (this.expandedIssueId() === id) {
      this.expandedIssueId.set(null);
      this.issueDetail.set(null);
      return;
    }

    this.issueDetail.set(null);
    this.detailLoading.set(true);
    this.expandedIssueId.set(id);
    this.loadDetail(id);
  }

  retryEligibility(id: string): void {
    this.retryingEligibility.set(true);
    this._http.post<void>(`/api/issues/${encodeURIComponent(id)}/retry-eligibility`, {}).subscribe({
      next: () => {
        this.retryingEligibility.set(false);
        this.loadDetail(id);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this.retryingEligibility.set(false);
        this._detailErrorSignal.set(LOAD_DETAIL_ERROR);
      },
    });
  }

  retryFailed(id: string): void {
    this._retryFailedErrorSignal.set(null);
    this._retryFailedSuccessSignal.set(null);
    this.retryingFailed.set(true);
    this._http.post<IssueDetail>(`/api/issues/${encodeURIComponent(id)}/retry`, {}).subscribe({
      next: () => {
        this.retryingFailed.set(false);
        this._retryFailedSuccessSignal.set(RETRY_FAILED_SUCCESS);
        this.loadDetail(id);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this.retryingFailed.set(false);
        this._retryFailedErrorSignal.set(RETRY_FAILED_ERROR);
      },
    });
  }

  private _upsertIssue(updated: IssueSummary): void {
    if (!SAFE_ID_RE.test(updated.id)) {
      console.warn('IssueService: rejected IssueUpdated event with invalid id');
      return;
    }

    const current = this.issues();
    const index = current.findIndex((i) => i.id === updated.id);

    if (index >= 0) {
      const next = [...current];
      next[index] = updated;
      this.issues.set(next);
    } else {
      this.issues.set([...current, updated]);
    }

    if (this.expandedIssueId() === updated.id) {
      this.loadDetail(updated.id);
    }
  }
}
