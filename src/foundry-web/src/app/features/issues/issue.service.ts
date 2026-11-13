import { DestroyRef, Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import {
  CodeStep,
  IssueDetail,
  IssueStage,
  IssueState,
  IssueSummary,
  LIVE_STATES,
  PublishPhase,
  QUEUED_TIER_STATES,
} from './issue.model';
import type { TokenTotal } from '../workers/run-totals.model';
import {
  DEFAULT_SELECTED_GROUPS,
  RESOLVED_GROUP_ID,
  STATE_GROUPS,
  StateGroup,
  StateGroupId,
  groupById,
  groupFor,
  groupRankFor,
  isKnownState,
  isResolvedState,
  withinGroupRankFor,
} from './issue-lifecycle.model';
import { components } from '../../api/schema';

type PagedIssues = components['schemas']['PagedIssues'];
// publishPhase (#787) is read defensively until the generated schema carries it.
type GeneratedIssueSummary = components['schemas']['IssueSummary'] & { publishPhase?: string | null };
type GeneratedTokenTotal = components['schemas']['TokenTotal'];

interface IssueCountsResponse {
  counts: Record<string, number>;
}

/** One rendered section of the active band: a selected, non-empty active group and its issues. */
export interface IssueSection {
  readonly group: StateGroup;
  readonly issues: readonly IssueSummary[];
}

const LOAD_ISSUES_ERROR = 'Failed to load issues';
const LOAD_RESOLVED_ERROR = 'Failed to load resolved issues';
const LOAD_MORE_RESOLVED_ERROR = 'Failed to load more resolved issues';
const LOAD_DETAIL_ERROR = 'Failed to load issue details';
const RETRY_ERROR = 'Failed to retry issue.';
const RETRY_SUCCESS = 'Retry queued. Issue status is updating.';
const RETRY_FRESH_ERROR = 'Failed to retry issue from a fresh branch.';
const RETRY_FRESH_BRANCH_ERROR = 'Could not delete the branch on the provider. The issue was not requeued.';
const RETRY_FRESH_SUCCESS = 'Branch discarded. Issue queued fresh; status is updating.';
const TRIAGE_FAILED_CONFLICT_ERROR = 'This issue is no longer in Triage failed.';

export type TriageAction = 'retry_triage' | 'skip_triage' | 'retriage';

interface TriageActionCopy {
  readonly path: string;
  readonly success: string;
  readonly conflictError: string;
  readonly error: string;
}

const TRIAGE_ACTIONS: Readonly<Record<TriageAction, TriageActionCopy>> = {
  retry_triage: {
    path: 'retry-triage',
    success: 'Triage queued again. Issue status is updating.',
    conflictError: TRIAGE_FAILED_CONFLICT_ERROR,
    error: 'Failed to retry triage.',
  },
  skip_triage: {
    path: 'skip-triage',
    success: 'Triage skipped. Issue status is updating.',
    conflictError: TRIAGE_FAILED_CONFLICT_ERROR,
    error: 'Failed to skip triage.',
  },
  retriage: {
    path: 'retriage',
    success: 'Re-triage queued. Issue status is updating.',
    conflictError:
      'Only queued or blocked issues can be re-triaged. It may have been dispatched already.',
    error: 'Failed to re-triage the issue.',
  },
};
// Issue ids are GUID-like; anything else (from the wire or a URL) is rejected before use.
export const SAFE_ID_RE = /^[\w-]+$/;
const COUNTS_DEBOUNCE_MS = 300;
// Safety-net interval for queue-order reconcile — covers order inputs that raise no IssueUpdated
// event (e.g. repository Position changes in Settings). ADR 0067: bounded staleness is acceptable
// for these infrequent operator actions; 30 s is low enough to reflect a Position reorder within
// one interval and high enough that idle traffic is one GET /api/issues per 30 s.
const RECONCILE_SAFETY_NET_MS = 30_000;

// Narrows the wire value to a known publish phase; anything else reads as no phase.
function toPublishPhase(value: string | null | undefined): PublishPhase | null {
  return value === 'publishing' || value === 'retrying' ? value : null;
}

const ISSUE_STAGES: ReadonlySet<string> = new Set<IssueStage>(['triage', 'implement', 'awaiting_review', 'done']);
const CODE_STEPS: ReadonlySet<string> = new Set<CodeStep>(['publishing', 'publish_retrying', 'rebasing', 'checks']);

// Narrows the wire value to a known stage; a stage this dashboard doesn't know reads as null, and
// the board leaves that card out rather than guess its column.
function toIssueStage(value: string | null | undefined): IssueStage | null {
  return value != null && ISSUE_STAGES.has(value) ? (value as IssueStage) : null;
}

function toCodeStep(value: string | null | undefined): CodeStep | null {
  return value != null && CODE_STEPS.has(value) ? (value as CodeStep) : null;
}

interface WirePlacement {
  stage?: string | null;
  needsAction?: boolean | null;
  awaitsDispatch?: boolean | null;
  codeStep?: string | null;
}

type Placement = Pick<IssueSummary, 'stage' | 'needsAction' | 'awaitsDispatch' | 'codeStep'>;

function toPlacement(wire: WirePlacement): Placement {
  return {
    stage: toIssueStage(wire.stage),
    needsAction: wire.needsAction === true,
    awaitsDispatch: wire.awaitsDispatch === true,
    codeStep: toCodeStep(wire.codeStep),
  };
}

function isDispatchableQueued(issue: IssueSummary): boolean {
  return (
    QUEUED_TIER_STATES.has(issue.state) &&
    issue.repositoryEligibilityStatus !== 'ineligible' &&
    issue.repositoryEligibilityStatus !== 'unreachable'
  );
}

@Injectable({ providedIn: 'root' })
export class IssueService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(IssueSignalRService);
  private readonly _destroyRef = inject(DestroyRef);

  readonly issues: WritableSignal<IssueSummary[]> = signal([]);
  readonly selectedIssueId: WritableSignal<string | null> = signal(null);
  readonly issueDetail: WritableSignal<IssueDetail | null> = signal(null);
  readonly detailLoading: WritableSignal<boolean> = signal(false);
  readonly initialLoading: WritableSignal<boolean> = signal(true);
  readonly retrying: WritableSignal<boolean> = signal(false);

  private readonly _loadErrorSignal: WritableSignal<string | null> = signal(null);
  readonly loadError: Signal<string | null> = this._loadErrorSignal.asReadonly();

  private readonly _detailErrorSignal: WritableSignal<string | null> = signal(null);
  readonly detailError: Signal<string | null> = this._detailErrorSignal.asReadonly();

  // True when the selected issue's detail answered 404: the issue is not tracked (or no longer is).
  private readonly _detailMissingSignal: WritableSignal<boolean> = signal(false);
  readonly detailMissing: Signal<boolean> = this._detailMissingSignal.asReadonly();

  private readonly _retryErrorSignal: WritableSignal<string | null> = signal(null);
  readonly retryError: Signal<string | null> = this._retryErrorSignal.asReadonly();

  private readonly _retrySuccessSignal: WritableSignal<string | null> = signal(null);
  readonly retrySuccess: Signal<string | null> = this._retrySuccessSignal.asReadonly();

  private readonly _triageActionPendingSignal: WritableSignal<TriageAction | null> = signal(null);
  readonly triageActionPending: Signal<TriageAction | null> =
    this._triageActionPendingSignal.asReadonly();

  private readonly _triageActionErrorSignal: WritableSignal<string | null> = signal(null);
  readonly triageActionError: Signal<string | null> = this._triageActionErrorSignal.asReadonly();

  private readonly _triageActionSuccessSignal: WritableSignal<string | null> = signal(null);
  readonly triageActionSuccess: Signal<string | null> =
    this._triageActionSuccessSignal.asReadonly();

  private readonly _countsSignal: WritableSignal<Record<string, number>> = signal({});
  readonly counts: Signal<Record<string, number>> = this._countsSignal.asReadonly();

  private readonly _selectedGroupsSignal: WritableSignal<ReadonlySet<StateGroupId>> = signal(DEFAULT_SELECTED_GROUPS);
  readonly selectedGroups: Signal<ReadonlySet<StateGroupId>> = this._selectedGroupsSignal.asReadonly();

  readonly resolvedSelected: Signal<boolean> = computed(() => this.selectedGroups().has(RESOLVED_GROUP_ID));

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

  private readonly _queueOrderStaleSignal: WritableSignal<boolean> = signal(false);
  readonly queueOrderStale: Signal<boolean> = this._queueOrderStaleSignal.asReadonly();

  private _detailSub: Subscription | null = null;
  private _countsDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private _reconcileIntervalHandle: ReturnType<typeof setInterval> | null = null;
  private _resolvedRequestToken = 0;
  private _reconcileRequestToken = 0;

  readonly sortedIssues: Signal<IssueSummary[]> = computed(() => {
    const all = this.issues();
    // Resolved states (currently only `completed`) never enter issues() — filtered in
    // loadIssues/_upsertIssue — so this sort never encounters them.

    // Build server-index map once so the comparator is O(1) per lookup (not O(n²) indexOf).
    const serverIndex = new Map<string, number>(all.map((issue, i) => [issue.id, i]));

    return [...all].sort((a, b) => {
      const rankA = groupRankFor(a.state);
      const rankB = groupRankFor(b.state);

      // Primary: group rank ascending (In progress → Needs attention → Waiting → ungrouped last).
      if (rankA !== rankB) {
        return rankA - rankB;
      }

      const withinRankA = withinGroupRankFor(a.state);
      const withinRankB = withinGroupRankFor(b.state);

      // Secondary: within-group tier rank ascending (queued-tier → detected → blocked).
      if (withinRankA !== withinRankB) {
        return withinRankA - withinRankB;
      }

      // Tertiary (both queued-tier): preserve raw server order (dispatch priority).
      if (QUEUED_TIER_STATES.has(a.state)) {
        return (serverIndex.get(a.id) ?? 0) - (serverIndex.get(b.id) ?? 0);
      }

      // Tertiary (same non-queued-tier): sort by detectedAt descending.
      return new Date(b.detectedAt).getTime() - new Date(a.detectedAt).getTime();
    });
  });

  readonly liveIssueCount: Signal<number> = computed(() =>
    this.issues().filter(i => LIVE_STATES.has(i.state)).length
  );

  // Read issues() (raw server order = DispatchOrderKey) — NOT sortedIssues().
  // The whole queued chain (queued, revision_queued, continuation_queued) sits in the Waiting
  // bucket, but serverIndex preserves their dispatch priority within that bucket. Using
  // sortedIssues() would still expose correct relative queue order within Waiting, but
  // issues() is canonical because it is unaffected by any future group reclassifications.
  readonly eligibleQueuedIssues: Signal<IssueSummary[]> = computed(() =>
    this.issues().filter(isDispatchableQueued)
  );

  // Same rationale: filter issues() to preserve server dispatch order.
  readonly ineligibleQueuedIssues: Signal<IssueSummary[]> = computed(() =>
    this.issues().filter(i => QUEUED_TIER_STATES.has(i.state) && !isDispatchableQueued(i))
  );

  readonly nextUpIssueId: Signal<string | null> = computed(() => {
    const first = this.eligibleQueuedIssues()[0];
    return first?.id ?? null;
  });

  readonly activeBandIssues: Signal<IssueSummary[]> = computed(() => {
    const selected = this.selectedGroups();
    return this.sortedIssues().filter(i => selected.has(groupFor(i.state).id));
  });

  // Splits the active band into one section per selected, non-empty active group, in STATE_GROUPS
  // order. sortedIssues() is already ordered by group rank, so concatenating the sections yields
  // exactly activeBandIssues() — queuePositions keeps indexing the rendered order.
  readonly activeSections: Signal<readonly IssueSection[]> = computed(() => {
    const byGroup = new Map<StateGroupId, IssueSummary[]>();
    for (const issue of this.activeBandIssues()) {
      const id = groupFor(issue.state).id;
      const bucket = byGroup.get(id);
      if (bucket === undefined) {
        byGroup.set(id, [issue]);
      } else {
        bucket.push(issue);
      }
    }
    const sections: IssueSection[] = [];
    for (const group of STATE_GROUPS) {
      const issues = byGroup.get(group.id);
      if (issues !== undefined) {
        sections.push({ group, issues });
      }
    }
    return sections;
  });

  // Positions index the rendered activeBandIssues() array so the ordinal always matches the
  // on-screen order (ADR 0067). Every queued-tier state lives in the Waiting group, so the queue is
  // either fully visible or fully hidden; ordinals are numbered contiguously from the topmost
  // visible queued card so it always shows "1".
  readonly queuePositions: Signal<ReadonlyMap<string, number>> = computed(() => {
    const positions = new Map<string, number>();
    let position = 1;
    for (const issue of this.activeBandIssues()) {
      if (isDispatchableQueued(issue)) {
        positions.set(issue.id, position++);
      }
    }
    return positions;
  });

  readonly nextUpAnnouncement: Signal<string> = computed(() => {
    const nextUpId = this.nextUpIssueId();
    if (nextUpId === null) {
      return '';
    }
    const nextUpIssue = this.issues().find(i => i.id === nextUpId);
    if (nextUpIssue === undefined) {
      return '';
    }
    return `Next up: issue #${nextUpIssue.issueNumber} — ${nextUpIssue.title}`;
  });

  readonly isEmpty: Signal<boolean> = computed(() => this.issues().length === 0);

  constructor() {
    this._signalR.on<IssueSummary>('IssueUpdated', (updated) => this._upsertIssue(updated));
    this._signalR.onReconnected(() => this.loadIssues());
    this._reconcileIntervalHandle = setInterval(() => {
      this._reconcileQueueOrder();
    }, RECONCILE_SAFETY_NET_MS);
    this._destroyRef.onDestroy(() => {
      if (this._countsDebounceHandle !== null) {
        clearTimeout(this._countsDebounceHandle);
      }
      if (this._reconcileIntervalHandle !== null) {
        clearInterval(this._reconcileIntervalHandle);
      }
    });
  }

  loadIssues(repositoryId?: string): void {
    this._loadErrorSignal.set(null);

    let params = new HttpParams();
    if (repositoryId !== undefined) {
      params = params.set('repositoryId', repositoryId);
    }

    this._http.get<PagedIssues>('/api/issues', { params }).subscribe({
      next: (response) => {
        this.issues.set(this._activeIssuesOnly(response.items));
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

  /** Sum of the per-state counts of every state in the group. */
  countForGroup(id: StateGroupId): number {
    return groupById(id).states.reduce((sum, state) => sum + this.countFor(state), 0);
  }

  isGroupSelected(id: StateGroupId): boolean {
    return this.selectedGroups().has(id);
  }

  toggleGroup(id: StateGroupId): void {
    const next = new Set<StateGroupId>(this.selectedGroups());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this._selectedGroupsSignal.set(next);
    if (id === RESOLVED_GROUP_ID) {
      this._onResolvedSelectionChanged();
    }
  }

  loadMoreResolved(repositoryId?: string): void {
    if (!this._resolvedCursor() || this.resolvedLoadingMore()) {
      return;
    }

    this._resolvedLoadMoreErrorSignal.set(null);
    this.resolvedLoadingMore.set(true);
    this._fetchResolvedPage(this._resolvedCursor(), repositoryId, false, this._resolvedRequestToken);
  }

  retryResolvedFetch(): void {
    this._onResolvedSelectionChanged();
  }

  loadDetail(id: string): void {
    this._detailSub?.unsubscribe();
    this._detailErrorSignal.set(null);
    this.detailLoading.set(true);
    this._detailSub = this._http.get<IssueDetail>(`/api/issues/${encodeURIComponent(id)}`).subscribe({
      next: (detail) => {
        const selected = this.selectedIssueId();
        if (selected !== null && selected !== id) {
          return;
        }
        this.issueDetail.set(detail);
        this.detailLoading.set(false);
        this._detailErrorSignal.set(null);
        this._detailMissingSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        const selected = this.selectedIssueId();
        if (selected !== null && selected !== id) {
          return;
        }
        console.error(err);
        this.detailLoading.set(false);
        this._detailErrorSignal.set(LOAD_DETAIL_ERROR);
        this._detailMissingSignal.set(err.status === 404);
      },
    });
  }

  /**
   * Opens the detail of the issue with `id`, or closes it for `null`. Selecting the open issue again
   * keeps it open (and reloads its detail).
   */
  selectIssue(id: string | null): void {
    this._retryErrorSignal.set(null);
    this._retrySuccessSignal.set(null);
    this.retrying.set(false);
    this._triageActionPendingSignal.set(null);
    this._triageActionErrorSignal.set(null);
    this._triageActionSuccessSignal.set(null);
    this._detailMissingSignal.set(false);

    if (id === null) {
      this.selectedIssueId.set(null);
      this.issueDetail.set(null);
      return;
    }

    this.issueDetail.set(null);
    this.detailLoading.set(true);
    this.selectedIssueId.set(id);
    this.loadDetail(id);
  }

  retryIssue(id: string): void {
    this._retryErrorSignal.set(null);
    this._retrySuccessSignal.set(null);
    this.retrying.set(true);
    this._http.post<IssueDetail>(`/api/issues/${encodeURIComponent(id)}/retry`, {}).subscribe({
      next: () => {
        this.retrying.set(false);
        this._retrySuccessSignal.set(RETRY_SUCCESS);
        this.loadDetail(id);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this.retrying.set(false);
        this._retryErrorSignal.set(RETRY_ERROR);
      },
    });
  }

  /**
   * Discards a continuable failure's branch on the provider and requeues the issue fresh, so the
   * next worker starts from the default branch instead of the previous commits.
   */
  retryIssueFresh(id: string): void {
    this._retryErrorSignal.set(null);
    this._retrySuccessSignal.set(null);
    this.retrying.set(true);
    this._http.post<IssueDetail>(`/api/issues/${encodeURIComponent(id)}/retry-fresh`, {}).subscribe({
      next: () => {
        this.retrying.set(false);
        this._retrySuccessSignal.set(RETRY_FRESH_SUCCESS);
        this.loadDetail(id);
      },
      error: (err: HttpErrorResponse) => {
        console.error(err);
        this.retrying.set(false);
        this._retryErrorSignal.set(err.status === 502 ? RETRY_FRESH_BRANCH_ERROR : RETRY_FRESH_ERROR);
      },
    });
  }

  /** Re-runs triage for an issue whose triage run failed (`triage_failed`). */
  retryTriage(id: string): void {
    this._runTriageAction(id, 'retry_triage');
  }

  /** Queues a `triage_failed` issue without a readiness check. */
  skipTriage(id: string): void {
    this._runTriageAction(id, 'skip_triage');
  }

  /** Sends a `queued` or `blocked` issue back through triage. */
  retriage(id: string): void {
    this._runTriageAction(id, 'retriage');
  }

  private _runTriageAction(id: string, action: TriageAction): void {
    const copy = TRIAGE_ACTIONS[action];
    this._triageActionErrorSignal.set(null);
    this._triageActionSuccessSignal.set(null);
    this._triageActionPendingSignal.set(action);
    this._http
      .post<IssueDetail>(`/api/issues/${encodeURIComponent(id)}/${copy.path}`, {})
      .subscribe({
        next: () => {
          this._triageActionPendingSignal.set(null);
          this._triageActionSuccessSignal.set(copy.success);
          this.loadDetail(id);
        },
        error: (err: HttpErrorResponse) => {
          console.error(err);
          this._triageActionPendingSignal.set(null);
          this._triageActionErrorSignal.set(err.status === 409 ? copy.conflictError : copy.error);
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

    // The hub payload is the server summary as sent, so its placement is narrowed here as the
    // mapper narrows a loaded one.
    updated = { ...updated, ...toPlacement(updated) };

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

    if (this.selectedIssueId() === updated.id) {
      this.loadDetail(updated.id);
    }

    this._scheduleDebouncedCountsRefetch();
  }

  private _prependToResolvedIfSelected(issue: IssueSummary): void {
    if (!this.resolvedSelected() || groupFor(issue.state).id !== RESOLVED_GROUP_ID) {
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
      this._reconcileQueueOrder();
    }, COUNTS_DEBOUNCE_MS);
  }

  // Filters and narrows the server response to active (non-resolved) issues with valid IDs and
  // known states — the same invariant enforced by loadIssues so that resolved states never enter
  // issues() and _reconcileQueueOrder cannot drift from loadIssues behaviour. Narrows the generated
  // schema type to the domain IssueSummary without a cast; the isKnownState type guard provides the
  // IssueState narrowing within the loop body.
  private _activeIssuesOnly(items: GeneratedIssueSummary[]): IssueSummary[] {
    const result: IssueSummary[] = [];
    for (const i of items) {
      if (!SAFE_ID_RE.test(i.id) || !isKnownState(i.state) || isResolvedState(i.state)) {
        continue;
      }
      result.push(this._toDomainIssueSummary(i, i.state));
    }
    return result;
  }

  // Maps a generated schema IssueSummary to the domain IssueSummary. Callers must pass the state
  // already narrowed to IssueState (via isKnownState type guard) to avoid an unsafe cast.
  // The generated schema uses number | string for numeric fields (openapi-typescript widens integer
  // formats); _toFiniteNumber normalizes them to the domain's stricter number type.
  private _toDomainIssueSummary(i: GeneratedIssueSummary, state: IssueState): IssueSummary {
    return {
      id: i.id,
      issueNumber: this._toFiniteNumber(i.issueNumber),
      title: i.title,
      state,
      repositorySlug: i.repositorySlug,
      detectedAt: i.detectedAt,
      url: i.url,
      failureClassification: i.failureClassification ?? undefined,
      // null is a valid domain value here (unknown eligibility), passed through intentionally;
      // unlike failureClassification, null is NOT coerced to undefined.
      repositoryEligibilityStatus: i.repositoryEligibilityStatus,
      runStats: i.runStats == null ? null : {
        runCount: this._toFiniteNumber(i.runStats.runCount),
        durationMs: i.runStats.durationMs === null ? null : this._toFiniteNumber(i.runStats.durationMs),
        numTurns: i.runStats.numTurns === null ? null : this._toFiniteNumber(i.runStats.numTurns),
        totalCostUsd: i.runStats.totalCostUsd === null ? null : this._toFiniteNumber(i.runStats.totalCostUsd),
        inputTokens: this._toTokenTotal(i.runStats.inputTokens),
        outputTokens: this._toTokenTotal(i.runStats.outputTokens),
      },
      pullRequestUrl: i.pullRequestUrl ?? null,
      providerType: i.providerType ?? null,
      publishPhase: toPublishPhase(i.publishPhase),
      checksOutcome: i.checksOutcome ?? null,
      ...toPlacement(i),
    };
  }

  // Coerces a number | string to a finite number. Non-finite results (NaN, Infinity) degrade to 0
  // so that domain state never contains NaN — which is typed as number but renders as "NaN" in the UI.
  private _toFiniteNumber(value: number | string): number {
    const n = Number(value);
    return Number.isFinite(n) ? n : 0;
  }

  private _toTokenTotal(tokens: GeneratedTokenTotal): TokenTotal {
    return {
      total: this._toFiniteNumber(tokens.total),
      knownRunCount: this._toFiniteNumber(tokens.knownRunCount),
      unknownRunCount: this._toFiniteNumber(tokens.unknownRunCount),
    };
  }

  // Filters generated schema items to those with valid IDs and known states, then maps to domain
  // IssueSummary. Used by the resolved page path where state may be resolved (unlike _activeIssuesOnly).
  private _toSafeDomainItems(items: GeneratedIssueSummary[]): IssueSummary[] {
    const result: IssueSummary[] = [];
    for (const i of items) {
      if (!SAFE_ID_RE.test(i.id) || !isKnownState(i.state)) {
        continue;
      }
      result.push(this._toDomainIssueSummary(i, i.state));
    }
    return result;
  }

  // Refetches GET /api/issues and replaces issues() with the server's array order, treating that
  // order as authoritative for dispatch priority (ADR 0067). Does NOT touch initialLoading or
  // _loadErrorSignal — those are owned by the initial-load path (loadIssues). A failed reconcile
  // sets _queueOrderStaleSignal to disclose bounded staleness; a successful reconcile clears it.
  // A latest-wins _reconcileRequestToken guards against a stale late response installing a wrong
  // order — the one defect that would silently recreate the drift bug this method was added to fix.
  private _reconcileQueueOrder(): void {
    this._reconcileRequestToken += 1;
    const token = this._reconcileRequestToken;

    this._http.get<PagedIssues>('/api/issues').subscribe({
      next: (response) => {
        if (token !== this._reconcileRequestToken) {
          return;
        }
        this.issues.set(this._activeIssuesOnly(response.items));
        this._queueOrderStaleSignal.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._reconcileRequestToken) {
          return;
        }
        if (!this.initialLoading()) {
          console.error(err);
          this._queueOrderStaleSignal.set(true);
        }
      },
    });
  }

  private _onResolvedSelectionChanged(): void {
    this._resolvedIssuesSignal.set([]);
    this._resolvedCursor.set(null);
    this._resolvedErrorSignal.set(null);
    this._resolvedLoadMoreErrorSignal.set(null);
    this._resolvedRequestToken += 1;

    if (!this.resolvedSelected()) {
      this.resolvedLoading.set(false);
      this.resolvedLoadingMore.set(false);
      return;
    }

    this.resolvedLoading.set(true);
    this._fetchResolvedPage(null, undefined, true, this._resolvedRequestToken);
  }

  private _fetchResolvedPage(
    cursor: string | null,
    repositoryId: string | undefined,
    isFirstPage: boolean,
    requestToken: number,
  ): void {
    let params = new HttpParams();
    for (const s of groupById(RESOLVED_GROUP_ID).states) {
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
        const items = page.items ?? [];
        const safeItems = this._toSafeDomainItems(items);
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
