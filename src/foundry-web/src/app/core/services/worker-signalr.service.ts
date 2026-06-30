import { Injectable, InjectionToken, Signal, WritableSignal, inject, signal } from '@angular/core';
import { Observable, Observer, Subject } from 'rxjs';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { WorkerActivity } from '../../features/workers/worker-run.model';

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

  private readonly _reconnected = new Subject<void>();
  readonly reconnected: Observable<void> = this._reconnected.asObservable();

  constructor() {
    this._hub = this._hubFactory();

    this._hub.on('WorkerActivity', (activity: WorkerActivity) => {
      this._workerActivitySignal.set(activity);
    });

    this._hub.onReconnected(() => {
      this._reconnected.next();
    });

    this._hub.start().catch(() => {
      console.warn('[WorkerSignalRService] Failed to connect to /hubs/workers');
    });
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
