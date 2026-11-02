import { DestroyRef, Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { SystemSignalRService } from './system-signalr.service';
import { UsageRefreshResponse, UsageRefreshState, UsageSnapshot } from '../models/usage.model';

/**
 * Safety net for the in-progress state: if no UsageSnapshotUpdated push arrives (e.g. the poll
 * faulted before broadcasting), fall back to idle. Exceeds the 90 s usage-report container timeout.
 */
export const USAGE_REFRESH_TIMEOUT_MS = 120_000;

/** How long a transient refresh outcome ("Up to date", refusal, error) stays visible. */
export const USAGE_REFRESH_STATUS_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class UsageService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  private readonly _snapshotSignal: WritableSignal<UsageSnapshot | null> = signal(null);
  readonly snapshot: Signal<UsageSnapshot | null> = this._snapshotSignal.asReadonly();

  private readonly _refreshStateSignal: WritableSignal<UsageRefreshState> = signal('idle');
  readonly refreshState: Signal<UsageRefreshState> = this._refreshStateSignal.asReadonly();

  private _refreshTimer: ReturnType<typeof setTimeout> | null = null;
  // Set when a push arrives while a refresh request is outstanding, so a push that beats the
  // 202 response still ends the in-progress state.
  private _pushedDuringRequest = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this._clearRefreshTimer());

    this._reseed()
      .pipe(takeUntilDestroyed())
      .subscribe();

    this._signalR.reconnected
      .pipe(
        switchMap(() => this._reseed()),
        takeUntilDestroyed()
      )
      .subscribe();

    this._signalR.usageSnapshotUpdated
      .pipe(takeUntilDestroyed())
      .subscribe((snapshot) => {
        this._snapshotSignal.set(snapshot);
        this._pushedDuringRequest = true;
        if (this._refreshStateSignal() === 'refreshing') {
          this._setRefreshState('idle');
        }
      });
  }

  /**
   * Requests an on-demand usage poll. The server returns no snapshot: the fresh figures arrive via
   * the UsageSnapshotUpdated push, which ends the in-progress state. No-op while already refreshing.
   */
  refresh(): void {
    if (this._refreshStateSignal() === 'refreshing') {
      return;
    }
    this._pushedDuringRequest = false;
    this._setRefreshState('refreshing', USAGE_REFRESH_TIMEOUT_MS);

    this._http.post<UsageRefreshResponse>('/api/credentials/usage/refresh', null).subscribe({
      next: (response) => {
        if (response.status === 'current') {
          this._setRefreshState('upToDate', USAGE_REFRESH_STATUS_MS);
        } else if (this._pushedDuringRequest) {
          this._setRefreshState('idle');
        }
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 409) {
          this._setRefreshState('unavailable', USAGE_REFRESH_STATUS_MS);
          return;
        }
        console.error('[UsageService] Failed to request usage refresh', err);
        this._setRefreshState('error', USAGE_REFRESH_STATUS_MS);
      },
    });
  }

  private _setRefreshState(state: UsageRefreshState, resetAfterMs?: number): void {
    this._clearRefreshTimer();
    this._refreshStateSignal.set(state);
    if (resetAfterMs !== undefined) {
      this._refreshTimer = setTimeout(() => {
        this._refreshTimer = null;
        this._refreshStateSignal.set('idle');
      }, resetAfterMs);
    }
  }

  private _clearRefreshTimer(): void {
    if (this._refreshTimer !== null) {
      clearTimeout(this._refreshTimer);
      this._refreshTimer = null;
    }
  }

  private _reseed(): Observable<void> {
    return this._http.get<UsageSnapshot | null>('/api/credentials/usage', { observe: 'response' }).pipe(
      map((response) => {
        this._snapshotSignal.set(response.status === 204 ? null : (response.body ?? null));
      }),
      catchError((err: HttpErrorResponse) => {
        console.error('[UsageService] Failed to fetch usage snapshot', err);
        return of(undefined);
      })
    );
  }
}
