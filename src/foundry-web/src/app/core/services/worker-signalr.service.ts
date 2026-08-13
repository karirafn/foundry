import { Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { Observable, Observer, Subject } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { WorkerActivity } from '../models/worker-activity.model';

export interface WorkerHub {
  on(methodName: string, callback: (activity: WorkerActivity) => void): void;
  onReconnected(callback: () => void): void;
  stream(methodName: string, workerRunId: string): {
    subscribe(callbacks: {
      next: (line: string) => void;
      error: (err: unknown) => void;
      complete: () => void;
    }): { dispose(): void };
  };
  start(): Promise<void>;
}

function buildWorkerHub(): WorkerHub {
  const conn: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/workers')
    .withAutomaticReconnect()
    .build();

  return {
    on: (methodName, callback) => conn.on(methodName, callback),
    onReconnected: (callback) => conn.onreconnected(callback),
    stream: (methodName, workerRunId) => conn.stream(methodName, workerRunId),
    start: () => conn.start(),
  };
}

export const WORKER_HUB_FACTORY = new InjectionToken<() => WorkerHub>(
  'WorkerHubFactory',
  { providedIn: 'root', factory: () => buildWorkerHub }
);

@Injectable({ providedIn: 'root' })
export class WorkerSignalRService {
  private readonly _hubFactory = inject(WORKER_HUB_FACTORY);
  private readonly _hub: WorkerHub;

  private readonly _workerActivitySignal: WritableSignal<WorkerActivity | null> = signal(null);
  readonly workerActivity: Signal<WorkerActivity | null> = this._workerActivitySignal.asReadonly();

  // Signal-backed maps so reads are reactive under OnPush.
  // Wrapping the entire map in a signal means any write causes a new reference,
  // which propagates to all computed/template reads that depend on it.
  private readonly _activityByRunIdSignal: WritableSignal<ReadonlyMap<string, string>> = signal(new Map());
  private readonly _activityByIssueIdSignal: WritableSignal<ReadonlyMap<string, string>> = signal(new Map());
  private readonly _commitCountByIssueIdSignal: WritableSignal<ReadonlyMap<string, number>> = signal(new Map());

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  constructor() {
    this._hub = this._hubFactory();

    this._hub.on('WorkerActivity', (activity: WorkerActivity) => {
      this._workerActivitySignal.set(activity);

      const nextByRunId = new Map(this._activityByRunIdSignal());
      nextByRunId.set(activity.workerRunId, activity.lastActivityAt);
      this._activityByRunIdSignal.set(nextByRunId);

      const nextByIssueId = new Map(this._activityByIssueIdSignal());
      nextByIssueId.set(activity.issueId, activity.lastActivityAt);
      this._activityByIssueIdSignal.set(nextByIssueId);

      const nextCommitCount = new Map(this._commitCountByIssueIdSignal());
      nextCommitCount.set(activity.issueId, activity.commitCount);
      this._commitCountByIssueIdSignal.set(nextCommitCount);
    });

    this._hub.onReconnected(() => {
      this._reconnected.next();
    });

    this._hub.start().catch(() => {
      console.warn('[WorkerSignalRService] Failed to connect to /hubs/workers');
    });
  }

  activityFor(workerRunId: string): string | null {
    return this._activityByRunIdSignal().get(workerRunId) ?? null;
  }

  activityForIssue(issueId: string): string | null {
    return this._activityByIssueIdSignal().get(issueId) ?? null;
  }

  /** Returns the observed commit count for the issue, or null if no WorkerActivity has arrived yet. */
  commitCountForIssue(issueId: string): number | null {
    return this._commitCountByIssueIdSignal().get(issueId) ?? null;
  }

  streamLog(workerRunId: string): Observable<string> {
    return new Observable((observer: Observer<string>) => {
      const subscription = this._hub.stream('StreamLog', workerRunId).subscribe({
        next: (line: string) => observer.next(line),
        error: (err: unknown) => observer.error(err),
        complete: () => observer.complete(),
      });

      return () => subscription.dispose();
    });
  }
}
