import { Injectable, Signal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, interval, of, startWith, switchMap } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { components } from '../../api/schema';

export type RateBudgetSnapshot = components['schemas']['RateBudgetSnapshot'];
export type ProviderBudgetHeadroom = components['schemas']['ProviderBudgetHeadroom'];

const REFRESH_INTERVAL_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class RateBudgetService {
  private readonly _http = inject(HttpClient);
  private readonly _snapshot = signal<RateBudgetSnapshot | null>(null);

  readonly snapshot: Signal<RateBudgetSnapshot | null> = this._snapshot.asReadonly();

  constructor() {
    interval(REFRESH_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() => this._fetch()),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  private _fetch(): Observable<RateBudgetSnapshot | null> {
    return this._http.get<RateBudgetSnapshot>('/api/rate-budget').pipe(
      tap((response) => {
        this._snapshot.set(response);
      }),
      catchError((err: HttpErrorResponse) => {
        console.error('[RateBudgetService] Failed to fetch rate budget snapshot', err);
        return of(null);
      })
    );
  }
}
