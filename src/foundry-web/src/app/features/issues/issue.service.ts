import { Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { SignalRService } from '../../core/services/signalr.service';
import { IssueDetail, IssueSummary } from './issue.model';

@Injectable({ providedIn: 'root' })
export class IssueService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SignalRService);

  readonly issues: WritableSignal<IssueSummary[]> = signal([]);
  readonly expandedIssueId: WritableSignal<string | null> = signal(null);
  readonly issueDetail: WritableSignal<IssueDetail | null> = signal(null);
  readonly detailLoading: WritableSignal<boolean> = signal(false);
  readonly loadError: WritableSignal<string | null> = signal(null);
  readonly detailError: WritableSignal<string | null> = signal(null);
  readonly initialLoading: WritableSignal<boolean> = signal(true);
  readonly retryingEligibility: WritableSignal<boolean> = signal(false);

  readonly sortedIssues: Signal<IssueSummary[]> = computed(() =>
    [...this.issues()].sort(
      (a, b) => new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime()
    )
  );

  readonly isEmpty: Signal<boolean> = computed(() => this.issues().length === 0);

  constructor() {
    this._signalR.on<IssueSummary>('IssueUpdated', (updated) => this._upsertIssue(updated));
    this._signalR.onReconnected(() => this.loadIssues());
  }

  loadIssues(repositoryId?: string): void {
    this.loadError.set(null);

    let params = new HttpParams();
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<IssueSummary[]>('/api/issues', { params }).subscribe({
      next: (issues) => {
        this.issues.set(issues);
        this.loadError.set(null);
        this.initialLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loadError.set(err.message);
        this.initialLoading.set(false);
      },
    });
  }

  loadDetail(id: string): void {
    this.detailError.set(null);
    this.detailLoading.set(true);
    this._http.get<IssueDetail>(`/api/issues/${id}`).subscribe({
      next: (detail) => {
        this.issueDetail.set(detail);
        this.detailLoading.set(false);
        this.detailError.set(null);
      },
      error: (err: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(err.message);
      },
    });
  }

  toggleExpand(id: string): void {
    if (this.expandedIssueId() === id) {
      this.expandedIssueId.set(null);
      this.issueDetail.set(null);
      return;
    }

    this.expandedIssueId.set(id);
    this.loadDetail(id);
  }

  retryEligibility(id: string): void {
    this.retryingEligibility.set(true);
    this._http.post<void>(`/api/issues/${id}/retry-eligibility`, {}).subscribe({
      next: () => {
        this.retryingEligibility.set(false);
        this.loadDetail(id);
      },
      error: (err: HttpErrorResponse) => {
        this.retryingEligibility.set(false);
        this.detailError.set(err.message);
      },
    });
  }

  private _upsertIssue(updated: IssueSummary): void {
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
