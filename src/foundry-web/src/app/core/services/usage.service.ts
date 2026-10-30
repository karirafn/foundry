import { Injectable, Signal, WritableSignal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { SystemSignalRService } from './system-signalr.service';
import { UsageSnapshot } from '../models/usage.model';

@Injectable({ providedIn: 'root' })
export class UsageService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

  private readonly _snapshotSignal: WritableSignal<UsageSnapshot | null> = signal(null);
  readonly snapshot: Signal<UsageSnapshot | null> = this._snapshotSignal.asReadonly();

  constructor() {
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
      });
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
