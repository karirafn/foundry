import { DestroyRef, Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { IssueDetail, IssueState, IssueSummary, LIVE_STATES, QUEUED_TIER_STATES } from './issue.model';
import { ACTIVE_STATES, RESOLVED_STATES, groupRankFor, isKnownState, isResolvedState } from './issue-lifecycle.model';

interface IssueCountsResponse {
  counts: Record<string, number>;
}

interface PagedIssues {
  items: IssueSummary[];
  nextCursor: string | null;
}

const LOAD_ISSUES_ERROR = 'Failed to load issues';
const LOAD_RESOLVED_ERROR = 'Failed to load resolved issues';
const LOAD_MORE_RESOLVED_ERROR = 'Failed to load more resolved issues';
const LOAD_DETAIL_ERROR = 'Failed to load issue details';
const RETRY_FAILED_ERROR = 'Failed to retry issue.';
const RETRY_FAILED_SUCCESS = 'Retry queued. Issue status is updating.';
const SAFE_ID_RE = /^[\w-]+$/;
const COUNTS_DEBOUNCE_MS = 300;

@Injectable({ providedIn: 'root' })
export class IssueService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(IssueSignalRService);
  private readonly _destroyRef = inject(DestroyRef);

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

  private readonly _selectedActiveStatesSignal: WritableSignal<ReadonlySet<IssueState>> = signal(ACTIVE_STATES);
  readonly selectedActiveStates: Signal<ReadonlySet<IssueState>> = this._selectedActiveStatesSignal.asReadonly();

  private readonly _selectedResolvedStatesSignal: WritableSignal<ReadonlySet<IssueState>> = signal(new Set<IssueState>());
  readonly selectedResolvedStates: Signal<ReadonlySet<IssueState>> = this._selectedResolvedStatesSignal.asReadonly();

  private readonly _resolvedIssuesSignal: WritableSignal<IssueSummary[]> = signal([]);
  readonly resolvedIssues: Signal<IssueSummary[]> = this._resolvedIssuesSignal.asReadonly();

  private readonly _resolvedCursor: WritableSignal<string | null> = signal(null);
  readonly hasMoreResolved: Signal<boolean> = computed(() => this._resolvedCursor() !== null);

  private readonly _resolvedErrorSignal: WritableSignal<string | null> = signal(null);
  readonly resolvedError: Signal<string | null> = this._resolvedErrorSignal.asReadonly();

  private readonly _resolvedLoadMoreErrorSignal: WritableSignal<string | null> = signal(null);
  readonly resolvedLoadMoreError: Signal<string | null> = this._resolvedLoadMoreErrorSignal.asReadonly();

  readonly resolvedLoading: WritableSignal<boolean> = signal(false);
  readonly resolvedLoadingMore: WritableSignal<boolean> = signal(false);

  private _detailSub: Subscription | null = null;
  private _countsDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private _resolvedRequestToken = 0;

  readonly sortedIssues: Signal<IssueSummary[]> = computed(() => {
    const all = this.issues();
    // Resolved states never enter issues() — filtered in loadIssues/_upsertIssue —
    // so this sort never encounters completed or unchanged.

    // Build server-index map once so the comparator is O(1) per lookup (not O(n²) indexOf).
    const serverIndex = new Map<string, number>(all.map((issue, i) => [issue.id, i]));

    return [...all].sort((a, b) => {
      const rankA = groupRankFor(a.state);
      const rankB = groupRankFor(b.state);

      // Primary: group rank ascending (In progress → Needs attention → Waiting → ungrouped last).
      if (rankA !== rankB) {
        return rankA - rankB;
      }

      const aIsQueued = QUEUED_TIER_STATES.has(a.state);
      const bIsQueued = QUEUED_TIER_STATES.has(b.state);

      // Secondary: within a bucket, non-queued cards sort before queued-tier cards.
      if (aIsQueued !== bIsQueued) {
        return aIsQueued ? 1 : -1;
      }

      // Tertiary (both queued): preserve raw server order (dispatch priority).
      if (aIsQueued) {
        return (serverIndex.get(a.id) ?? 0) - (serverIndex.get(b.id) ?? 0);
      }

      // Tertiary (both non-queued): sort by detectedAt descending.
      return new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime();
    });
  });

  readonly liveIssueCount: Signal<number> = computed(() =>
    this.issues().filter(i => LIVE_STATES.has(i.state)).length
  );

  // Read issues() (raw server order = DispatchOrderKey) — NOT sortedIssues().
  // Step 2's bucket sort splits the queued chain across visual groups (continuation_queued
  // lands in "In progress", queued/revision_queued land in "Waiting"), so sortedIssues()
  // no longer reflects true server dispatch priority. Dispatch order must follow issues().
  readonly eligibleQueuedIssues: Signal<IssueSummary[]> = computed(() =>
    this.issues().filter(i =>
      QUEUED_TIER_STATES.has(i.state) &&
      i.repositoryEligibilityStatus !== 'ineligible' &&
      i.repositoryEligibilityStatus !== 'unreachable'
    )
  );

  // Same rationale: filter issues() to preserve server dispatch order.
  readonly ineligibleQueuedIssues: Signal<IssueSummary[]> = computed(() =>
    this.issues().filter(i =>
      QUEUED_TIER_STATES.has(i.state) &&
      (i.repositoryEligibilityStatus === 'ineligible' || i.repositoryEligibilityStatus === 'unreachable')
    )
  );

  readonly nextUpIssueId: Signal<string | null> = computed(() => {
    const first = this.eligibleQueuedIssues()[0];
    return first?.id ?? null;
  });

  readonly activeBandIssues: Signal<IssueSummary[]> = computed(() =>
    this.sortedIssues().filter(i =>
      i.state === 'ineligible' || this.selectedActiveStates().has(i.state)
    )
  );

  readonly isEmpty: Signal<boolean> = computed(() => this.issues().length === 0);

  readonly activeFilterCount: Signal<number> = computed(() => {
    const selectedActive = this.selectedActiveStates();
    const selectedResolved = this.selectedResolvedStates();
    const deselectedActive = [...ACTIVE_STATES].filter(s => !selectedActive.has(s)).length;
    const selectedResolvedCount = [...RESOLVED_STATES].filter(s => selectedResolved.has(s)).length;
    return deselectedActive + selectedResolvedCount;
  });

  constructor() {
    this._signalR.on<IssueSummary>('IssueUpdated', (updated) => this._upsertIssue(updated));
    this._signalR.onReconnected(() => this.loadIssues());
    this._destroyRef.onDestroy(() => {
      if (this._countsDebounceHandle !== null) {
        clearTimeout(this._countsDebounceHandle);
      }
    });
  }

  loadIssues(repositoryId?: string): void {
    this._loadErrorSignal.set(null);

    let params = new HttpParams();
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<IssueSummary[]>('/api/issues', { params }).subscribe({
      next: (issues) => {
        this.issues.set(issues.filter(i => SAFE_ID_RE.test(i.id) && isKnownState(i.state) && !isResolvedState(i.state)));
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

  isStateSelected(state: IssueState): boolean {
    if (isResolvedState(state)) {
      return this.selectedResolvedStates().has(state);
    }
    return this.selectedActiveStates().has(state);
  }

  toggleState(state: IssueState): void {
    if (isResolvedState(state)) {
      const current = this.selectedResolvedStates();
      const next = new Set<IssueState>(current);
      if (next.has(state)) {
        next.delete(state);
      } else {
        next.add(state);
      }
      this._selectedResolvedStatesSignal.set(next);
      this._onResolvedSelectionChanged(next);
    } else {
      const current = this.selectedActiveStates();
      const next = new Set<IssueState>(current);
      if (next.has(state)) {
        next.delete(state);
      } else {
        next.add(state);
      }
      this._selectedActiveStatesSignal.set(next);
    }
  }

  loadMoreResolved(repositoryId?: string): void {
    if (!this._resolvedCursor() || this.resolvedLoadingMore()) {
      return;
    }

    this._resolvedLoadMoreErrorSignal.set(null);
    this.resolvedLoadingMore.set(true);
    this._fetchResolvedPage(this.selectedResolvedStates(), this._resolvedCursor(), repositoryId, false, this._resolvedRequestToken);
  }

  retryResolvedFetch(): void {
    this._onResolvedSelectionChanged(this.selectedResolvedStates());
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

    if (!isKnownState(updated.state)) {
      console.warn('IssueService: rejected IssueUpdated event with unknown state');
      return;
    }

    const current = this.issues();
    const index = current.findIndex((i) => i.id === updated.id);

    if (isResolvedState(updated.state)) {
      if (index >= 0) {
        this.issues.set(current.filter((i) => i.id !== updated.id));
      }
      this._prependToResolvedIfSelected(updated);
    } else {
      if (index >= 0) {
        const next = [...current];
        next[index] = updated;
        this.issues.set(next);
      } else {
        this.issues.set([...current, updated]);
      }
      this._removeFromResolved(updated.id);
    }

    if (this.expandedIssueId() === updated.id) {
      this.loadDetail(updated.id);
    }

    this._scheduleDebouncedCountsRefetch();
  }

  private _prependToResolvedIfSelected(issue: IssueSummary): void {
    if (!this.selectedResolvedStates().has(issue.state)) {
      return;
    }
    const existing = this._resolvedIssuesSignal();
    const withoutDuplicate = existing.filter((i) => i.id !== issue.id);
    this._resolvedIssuesSignal.set([issue, ...withoutDuplicate]);
  }

  private _removeFromResolved(id: string): void {
    const current = this._resolvedIssuesSignal();
    const next = current.filter(i => i.id !== id);
    if (next.length !== current.length) {
      this._resolvedIssuesSignal.set(next);
    }
  }

  private _scheduleDebouncedCountsRefetch(): void {
    if (this._countsDebounceHandle !== null) {
      clearTimeout(this._countsDebounceHandle);
    }
    this._countsDebounceHandle = setTimeout(() => {
      this._countsDebounceHandle = null;
      this.loadCounts();
    }, COUNTS_DEBOUNCE_MS);
  }

  private _onResolvedSelectionChanged(states: ReadonlySet<IssueState>): void {
    this._resolvedIssuesSignal.set([]);
    this._resolvedCursor.set(null);
    this._resolvedErrorSignal.set(null);
    this._resolvedLoadMoreErrorSignal.set(null);
    this._resolvedRequestToken += 1;

    if (states.size === 0) {
      return;
    }

    this.resolvedLoading.set(true);
    this._fetchResolvedPage(states, null, undefined, true, this._resolvedRequestToken);
  }

  private _fetchResolvedPage(
    states: ReadonlySet<IssueState>,
    cursor: string | null,
    repositoryId: string | undefined,
    isFirstPage: boolean,
    requestToken: number,
  ): void {
    let params = new HttpParams();
    for (const s of states) {
      params = params.append('states', s);
    }
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<PagedIssues>('/api/issues', { params }).subscribe({
      next: (page) => {
        if (requestToken !== this._resolvedRequestToken) {
          return;
        }
        if (!Array.isArray(page?.items)) {
          if (isFirstPage) {
            this._resolvedErrorSignal.set(LOAD_RESOLVED_ERROR);
            this.resolvedLoading.set(false);
          } else {
            this._resolvedLoadMoreErrorSignal.set(LOAD_MORE_RESOLVED_ERROR);
            this.resolvedLoadingMore.set(false);
          }
          return;
        }
        const safeItems = page.items.filter(i => SAFE_ID_RE.test(i.id) && isKnownState(i.state));
        if (isFirstPage) {
          this._resolvedIssuesSignal.set(safeItems);
        } else {
          const existing = this._resolvedIssuesSignal();
          const existingIds = new Set(existing.map(i => i.id));
          const newItems = safeItems.filter(i => !existingIds.has(i.id));
          this._resolvedIssuesSignal.set([...existing, ...newItems]);
        }
        this._resolvedCursor.set(page.nextCursor);
        if (isFirstPage) {
          this.resolvedLoading.set(false);
        } else {
          this.resolvedLoadingMore.set(false);
        }
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        if (isFirstPage) {
          this._resolvedErrorSignal.set(LOAD_RESOLVED_ERROR);
          this.resolvedLoading.set(false);
        } else {
          this._resolvedLoadMoreErrorSignal.set(LOAD_MORE_RESOLVED_ERROR);
          this.resolvedLoadingMore.set(false);
        }
      },
    });
  }
}
