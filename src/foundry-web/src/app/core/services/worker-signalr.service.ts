import { Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { Observable, Observer, Subject } from 'rxjs';
import { WorkerActivity } from '../models/worker-activity.model';
import { AgentActivity, AgentActivityEnded, InFlightTool } from '../models/agent-activity.model';
import { LoadOutcome } from '../models/load-outcome';
import { ConnectionStatus, HubConnections, SharedHubConnection } from './hub-connection';

/** How many recent tool calls the Floor's tool ticker keeps per run. */
export const RECENT_TOOL_LIMIT = 5;

/** The workers hub method that returns the current activity of every active run. */
export const GET_CURRENT_ACTIVITY = 'GetCurrentActivity';

/** What `GetCurrentActivity` returns. */
export interface CurrentWorkerActivity {
  readonly workerActivities: readonly WorkerActivity[];
  readonly agentActivities: readonly AgentActivity[];
}

/** Live messages that arrive while the current activity is loading; they may be newer than it. */
interface ArrivedDuringLoad {
  readonly agentActivities: Map<string, AgentActivity>;
  readonly endedRunIds: Set<string>;
}

function eventTime(activity: AgentActivity): number {
  return activity.lastEventAt === null ? 0 : Date.parse(activity.lastEventAt);
}

@Injectable({ providedIn: 'root' })
export class WorkerSignalRService {
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

  private _activityLoad: Promise<LoadOutcome> | null = null;
  private _arrivedDuringLoad: ArrivedDuringLoad | null = null;

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
      this._arrivedDuringLoad?.agentActivities.set(activity.issueId, activity);
      const current = this._agentActivityByIssueIdSignal();
      const replacedRunId = current.get(activity.issueId)?.workerRunId;
      const next = new Map(current);
      next.set(activity.issueId, activity);
      this._agentActivityByIssueIdSignal.set(next);
      this._recordTool(activity, replacedRunId !== undefined && replacedRunId !== activity.workerRunId ? replacedRunId : null);
    });

    this._hub.on<AgentActivityEnded>('AgentActivityEnded', (ended) => {
      this._arrivedDuringLoad?.endedRunIds.add(ended.workerRunId);
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

    // Events may have been missed while disconnected, so every connect replaces the state.
    this._hub.onConnected(() => {
      void this.loadActivity();
      this._reconnected.next();
    });

    this._hub.start().then(
      () => void this.loadActivity(),
      () => console.warn('[WorkerSignalRService] Failed to connect to /hubs/workers'),
    );
  }

  /**
   * Replaces the held activity with the hub's current activity, so a run that ended while disconnected
   * is dropped. A load already in flight is shared rather than repeated.
   */
  loadActivity(): Promise<LoadOutcome> {
    this._activityLoad ??= this._fetchActivity().finally(() => {
      this._activityLoad = null;
    });
    return this._activityLoad;
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

  private async _fetchActivity(): Promise<LoadOutcome> {
    const arrived: ArrivedDuringLoad = { agentActivities: new Map(), endedRunIds: new Set() };
    this._arrivedDuringLoad = arrived;
    try {
      const current = await this._hub.invoke<CurrentWorkerActivity>(GET_CURRENT_ACTIVITY);
      this._applyCurrent(current, arrived);
      return 'loaded';
    } catch (error: unknown) {
      console.warn('[WorkerSignalRService] Failed to load the current activity', error);
      return 'failed';
    } finally {
      this._arrivedDuringLoad = null;
    }
  }

  private _applyCurrent(current: CurrentWorkerActivity, arrived: ArrivedDuringLoad): void {
    this._activityByRunIdSignal.set(new Map(current.workerActivities.map((a) => [a.workerRunId, a.lastActivityAt])));
    this._activityByIssueIdSignal.set(new Map(current.workerActivities.map((a) => [a.issueId, a.lastActivityAt])));

    const next = new Map(current.agentActivities.map((a) => [a.issueId, a]));
    // A message that arrived during the load may be newer than the snapshot.
    for (const [issueId, activity] of arrived.agentActivities) {
      const loaded = next.get(issueId);
      if (loaded === undefined || loaded.workerRunId !== activity.workerRunId || eventTime(activity) >= eventTime(loaded)) {
        next.set(issueId, activity);
      }
    }
    for (const [issueId, activity] of next) {
      if (arrived.endedRunIds.has(activity.workerRunId)) {
        next.delete(issueId);
      }
    }
    this._agentActivityByIssueIdSignal.set(next);

    const liveRunIds = new Set([...next.values()].map((a) => a.workerRunId));
    for (const workerRunId of this._recentToolsByRunIdSignal().keys()) {
      if (!liveRunIds.has(workerRunId)) {
        this._forgetTools(workerRunId);
      }
    }
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
