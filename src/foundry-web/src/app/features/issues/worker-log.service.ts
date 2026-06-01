import { Injectable, InjectionToken, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { IssueState } from './issue.model';
import { WorkerReportSummary } from './worker-report.model';

const LIVE_STATES: ReadonlySet<string> = new Set<IssueState>(['in_progress', 'revision_in_progress']);

export interface WorkerLogHub {
  on(methodName: string, callback: (report: WorkerReportSummary) => void): void;
  off(methodName: string): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<void>;
}

function buildWorkerLogHub(): WorkerLogHub {
  const conn: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/worker-log')
    .withAutomaticReconnect()
    .build();

  return {
    on: (methodName, callback) => conn.on(methodName, callback),
    off: (methodName) => conn.off(methodName),
    start: () => conn.start(),
    stop: () => conn.stop(),
    invoke: (methodName, ...args) => conn.invoke(methodName, ...args),
  };
}

export const WORKER_LOG_HUB_FACTORY = new InjectionToken<() => WorkerLogHub>(
  'WorkerLogHubFactory',
  { providedIn: 'root', factory: () => buildWorkerLogHub }
);

@Injectable({ providedIn: 'root' })
export class WorkerLogService {
  private readonly _http = inject(HttpClient);
  private readonly _hubFactory = inject(WORKER_LOG_HUB_FACTORY);

  private readonly _reports: WritableSignal<WorkerReportSummary[]> = signal([]);
  private readonly _loading: WritableSignal<boolean> = signal(false);
  private readonly _error: WritableSignal<string | null> = signal(null);
  private readonly _isLive: WritableSignal<boolean> = signal(false);
  private readonly _activeWorkerRunId: WritableSignal<string | null> = signal(null);
  private readonly _activeIssueId: WritableSignal<string | null> = signal(null);

  readonly reports: Signal<WorkerReportSummary[]> = computed(() => this._reports());
  readonly loading: Signal<boolean> = computed(() => this._loading());
  readonly error: Signal<string | null> = computed(() => this._error());
  readonly isLive: Signal<boolean> = computed(() => this._isLive());
  readonly activeWorkerRunId: Signal<string | null> = computed(() => this._activeWorkerRunId());

  private _hub: WorkerLogHub | null = null;

  open(workerRunId: string, issueId: string, issueState: string): void {
    this._activeWorkerRunId.set(workerRunId);
    this._activeIssueId.set(issueId);
    this._isLive.set(LIVE_STATES.has(issueState));
    this._error.set(null);
    this._loading.set(true);

    this._loadReports(workerRunId);

    if (this._isLive()) {
      this._connectHub(issueId);
    }
  }

  close(): void {
    this._disconnectHub();
    this._reports.set([]);
    this._loading.set(false);
    this._error.set(null);
    this._isLive.set(false);
    this._activeWorkerRunId.set(null);
    this._activeIssueId.set(null);
  }

  private _loadReports(workerRunId: string): void {
    this._http.get<WorkerReportSummary[]>(`/api/workers/${workerRunId}/reports`).subscribe({
      next: (reports) => {
        this._reports.set([...reports].sort((a, b) => a.sequenceNumber - b.sequenceNumber));
        this._loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this._error.set(err.message);
        this._loading.set(false);
      },
    });
  }

  private _connectHub(issueId: string): void {
    this._hub = this._hubFactory();

    this._hub.on('ReportReceived', (report: WorkerReportSummary) => {
      this._reports.update((current) =>
        [...current, report].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
      );
    });

    this._hub
      .start()
      .then(() => this._hub?.invoke('JoinIssueLog', issueId))
      .catch(() => {});
  }

  private _disconnectHub(): void {
    const hub = this._hub;
    const issueId = this._activeIssueId();

    if (hub === null || issueId === null) {
      return;
    }

    hub
      .invoke('LeaveIssueLog', issueId)
      .catch(() => {})
      .finally(() => {
        hub.off('ReportReceived');
        hub.stop().catch(() => {});
        this._hub = null;
      });
  }
}
