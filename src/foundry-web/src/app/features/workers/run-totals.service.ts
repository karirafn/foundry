import { DestroyRef, Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { IssueSignalRService } from '../../core/services/issue-signalr.service';
import { RunTotals, StatsScope } from './run-totals.model';

const REFETCH_DEBOUNCE_MS = 300;

@Injectable({ providedIn: 'root' })
export class WorkerRunTotalsService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(IssueSignalRService);
  private readonly _destroyRef = inject(DestroyRef);

  private readonly _totalsSignal: WritableSignal<RunTotals | null> = signal(null);
  readonly totals: Signal<RunTotals | null> = this._totalsSignal.asReadonly();

  private readonly _loadingSignal: WritableSignal<boolean> = signal(false);
  readonly loading: Signal<boolean> = this._loadingSignal.asReadonly();

  private readonly _errorSignal: WritableSignal<string | null> = signal(null);
  readonly error: Signal<string | null> = this._errorSignal.asReadonly();

  private readonly _scopeSignal: WritableSignal<StatsScope> = signal('month');
  readonly scope: Signal<StatsScope> = this._scopeSignal.asReadonly();

  private _requestToken = 0;
  private _debounceHandle: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this._signalR.on('IssueUpdated', () => this._scheduleDebounced());
    this._signalR.onReconnected(() => this._scheduleDebounced());
    this._destroyRef.onDestroy(() => {
      if (this._debounceHandle !== null) {
        clearTimeout(this._debounceHandle);
      }
    });
  }

  fetch(): void {
    this._loadingSignal.set(true);
    this._errorSignal.set(null);
    this._requestToken += 1;
    const token = this._requestToken;
    const params = this._buildParams();

    this._http.get<RunTotals>('/api/workers/run-totals', { params }).subscribe({
      next: (totals) => {
        if (token !== this._requestToken) {
          return;
        }
        this._totalsSignal.set(totals);
        this._loadingSignal.set(false);
        this._errorSignal.set(null);
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._requestToken) {
          return;
        }
        console.error(err);
        this._loadingSignal.set(false);
        this._errorSignal.set('Failed to load run totals');
      },
    });
  }

  setScope(scope: StatsScope): void {
    this._scopeSignal.set(scope);
    this.fetch();
  }

  private _scheduleDebounced(): void {
    if (this._debounceHandle !== null) {
      clearTimeout(this._debounceHandle);
    }
    this._debounceHandle = setTimeout(() => {
      this._debounceHandle = null;
      this._refetchInPlace();
    }, REFETCH_DEBOUNCE_MS);
  }

  private _refetchInPlace(): void {
    // In-place refresh: does NOT set loading to true (no skeleton flash).
    this._requestToken += 1;
    const token = this._requestToken;
    const params = this._buildParams();

    this._http.get<RunTotals>('/api/workers/run-totals', { params }).subscribe({
      next: (totals) => {
        if (token !== this._requestToken) {
          return;
        }
        this._totalsSignal.set(totals);
        this._errorSignal.set(null);
      },
      error: (err: HttpErrorResponse) => {
        if (token !== this._requestToken) {
          return;
        }
        console.error(err);
        this._errorSignal.set('Failed to load run totals');
      },
    });
  }

  private _buildParams(): HttpParams {
    if (this._scopeSignal() === 'all') {
      return new HttpParams();
    }

    const now = new Date();
    const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
    return new HttpParams()
      .set('from', monthStart.toISOString())
      .set('to', now.toISOString());
  }
}
