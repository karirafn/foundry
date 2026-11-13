import { DestroyRef, Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { Observable, Observer, Subject } from 'rxjs';
import { WorkerActivity } from '../models/worker-activity.model';
import { AgentActivity, AgentActivityEnded, InFlightTool } from '../models/agent-activity.model';
import { ConnectionStatus, HubConnections, SharedHubConnection } from './hub-connection';

/** How many recent tool calls the Floor's tool ticker keeps per run. */
export const RECENT_TOOL_LIMIT = 5;

/**
 * After a reconnect the hub replays the payload of every active run. Entries not re-sent within
 * this window belong to runs that ended while the connection was down (their AgentActivityEnded
 * was missed) and are pruned.
 */
export const RECONNECT_REPLAY_GRACE_MS = 10_000;

@Injectable({ providedIn: 'root' })
export class WorkerSignalRService {
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _hub: SharedHubConnection = inject(HubConnections).get('workers');

  private readonly _workerActivitySignal: WritableSignal<WorkerActivity | null> = signal(null);
  readonly workerActivity: Signal<WorkerActivity | null> = this._workerActivitySignal.asReadonly();

  // Signal-backed maps so reads are reactive under OnPush.
  // Wrapping the entire map in a signal means any write causes a new reference,
  // which propagates to all computed/template reads that depend on it.
  private readonly _activityByRunIdSignal: WritableSignal<ReadonlyMap<string, string>> = signal(new Map());
  private readonly _activityByIssueIdSignal: WritableSignal<ReadonlyMap<string, string>> = signal(new Map());

  private readonly _agentActivityByIssueIdSignal: WritableSignal<ReadonlyMap<string, AgentActivity>> = signal(new Map());
  private readonly _recentToolsByRunIdSignal: WritableSignal<ReadonlyMap<string, readonly InFlightTool[]>> = signal(new Map());

  /** Every live Agent Activity projection, one per active run, in arrival order. */
  readonly agentActivities: Signal<readonly AgentActivity[]> = computed(() => [...this._agentActivityByIssueIdSignal().values()]);

  /** Number of active runs reporting Agent Activity — the "agents working" count. */
  readonly activeAgentCount: Signal<number> = computed(() => this._agentActivityByIssueIdSignal().size);

  readonly connectionStatus: Signal<ConnectionStatus> = this._hub.status;

  // Issue ids held when the connection dropped and not yet re-sent by the reconnect replay.
  private _unconfirmedIssueIds: Set<string> | null = null;
  private _replayGraceHandle: ReturnType<typeof setTimeout> | null = null;

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  constructor() {
    this._hub.on<WorkerActivity>('WorkerActivity', (activity) => {
      this._workerActivitySignal.set(activity);

      const nextByRunId = new Map(this._activityByRunIdSignal());
      nextByRunId.set(activity.workerRunId, activity.lastActivityAt);
      this._activityByRunIdSignal.set(nextByRunId);

      const nextByIssueId = new Map(this._activityByIssueIdSignal());
      nextByIssueId.set(activity.issueId, activity.lastActivityAt);
      this._activityByIssueIdSignal.set(nextByIssueId);
    });

    this._hub.on<AgentActivity>('AgentActivity', (activity) => {
      this._unconfirmedIssueIds?.delete(activity.issueId);
      const current = this._agentActivityByIssueIdSignal();
      const replacedRunId = current.get(activity.issueId)?.workerRunId;
      const next = new Map(current);
      next.set(activity.issueId, activity);
      this._agentActivityByIssueIdSignal.set(next);
      this._recordTool(activity, replacedRunId !== undefined && replacedRunId !== activity.workerRunId ? replacedRunId : null);
    });

    this._hub.on<AgentActivityEnded>('AgentActivityEnded', (ended) => {
      const current = this._agentActivityByIssueIdSignal();
      // A newer run for the same issue may already have replaced the entry.
      if (current.get(ended.issueId)?.workerRunId !== ended.workerRunId) {
        return;
      }
      const next = new Map(current);
      next.delete(ended.issueId);
      this._agentActivityByIssueIdSignal.set(next);
      this._forgetTools(ended.workerRunId);
    });

    this._hub.onReconnecting(() => {
      // A prune scheduled by an earlier reconnect must not fire while the connection is down again.
      if (this._replayGraceHandle !== null) {
        clearTimeout(this._replayGraceHandle);
        this._replayGraceHandle = null;
      }
      this._unconfirmedIssueIds ??= new Set(this._agentActivityByIssueIdSignal().keys());
    });

    this._hub.onReconnected(() => {
      this._scheduleUnconfirmedPrune();
      this._reconnected.next();
    });

    this._destroyRef.onDestroy(() => {
      if (this._replayGraceHandle !== null) {
        clearTimeout(this._replayGraceHandle);
      }
    });

    this._hub.start().catch(() => {
      console.warn('[WorkerSignalRService] Failed to connect to /hubs/workers');
    });
  }

  /** Recent tool calls for the run, newest last, accumulated from successive in-flight tools. */
  recentToolsForRun(workerRunId: string): readonly InFlightTool[] {
    return this._recentToolsByRunIdSignal().get(workerRunId) ?? [];
  }

  activityFor(workerRunId: string): string | null {
    return this._activityByRunIdSignal().get(workerRunId) ?? null;
  }

  activityForIssue(issueId: string): string | null {
    return this._activityByIssueIdSignal().get(issueId) ?? null;
  }

  /** Returns the live Agent Activity projection for the issue's active run, or null if none has arrived. */
  agentActivityForIssue(issueId: string): AgentActivity | null {
    return this._agentActivityByIssueIdSignal().get(issueId) ?? null;
  }

  private _recordTool(activity: AgentActivity, replacedRunId: string | null): void {
    const tool = activity.inFlightTool;
    const current = this._recentToolsByRunIdSignal();
    const existing = current.get(activity.workerRunId) ?? [];
    const last = existing.length > 0 ? existing[existing.length - 1] : null;
    const isNewTool = tool !== null && (last === null
      || last.name !== tool.name || last.summary !== tool.summary || last.startedAt !== tool.startedAt);
    if (!isNewTool && replacedRunId === null) {
      return;
    }
    const next = new Map(current);
    if (replacedRunId !== null) {
      next.delete(replacedRunId);
    }
    if (isNewTool) {
      next.set(activity.workerRunId, [...existing, tool].slice(-RECENT_TOOL_LIMIT));
    }
    this._recentToolsByRunIdSignal.set(next);
  }

  private _forgetTools(workerRunId: string): void {
    const current = this._recentToolsByRunIdSignal();
    if (!current.has(workerRunId)) {
      return;
    }
    const next = new Map(current);
    next.delete(workerRunId);
    this._recentToolsByRunIdSignal.set(next);
  }

  private _scheduleUnconfirmedPrune(): void {
    if (this._replayGraceHandle !== null) {
      clearTimeout(this._replayGraceHandle);
    }
    this._replayGraceHandle = setTimeout(() => {
      this._replayGraceHandle = null;
      const unconfirmed = this._unconfirmedIssueIds;
      this._unconfirmedIssueIds = null;
      if (unconfirmed === null || unconfirmed.size === 0) {
        return;
      }
      const current = this._agentActivityByIssueIdSignal();
      const next = new Map(current);
      for (const issueId of unconfirmed) {
        const stale = next.get(issueId);
        if (stale !== undefined) {
          next.delete(issueId);
          this._forgetTools(stale.workerRunId);
        }
      }
      this._agentActivityByIssueIdSignal.set(next);
    }, RECONNECT_REPLAY_GRACE_MS);
  }

  streamLog(workerRunId: string): Observable<string> {
    return new Observable((observer: Observer<string>) => {
      const subscription = this._hub.stream<string>('StreamLog', workerRunId).subscribe({
        next: (line: string) => observer.next(line),
        error: (err: unknown) => observer.error(err),
        complete: () => observer.complete(),
      });

      return () => subscription.dispose();
    });
  }
}
