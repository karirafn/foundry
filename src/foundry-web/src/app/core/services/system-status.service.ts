import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, switchMap, tap } from 'rxjs/operators';
import { SystemSignalRService } from './system-signalr.service';
import { SystemStatusResponse } from '../models/system-status.model';

@Injectable({ providedIn: 'root' })
export class SystemStatusService {
  private readonly _http = inject(HttpClient);
  private readonly _signalR = inject(SystemSignalRService);

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
  }

  private _reseed(): Observable<SystemStatusResponse | null> {
    return this._http.get<SystemStatusResponse>('/api/system/status').pipe(
      tap((response) => {
        this._signalR.applyDockerAvailability(response.dockerAvailable);
      }),
      catchError((err: HttpErrorResponse) => {
        console.error('[SystemStatusService] Failed to fetch system status', err);
        return of(null);
      })
    );
  }
}
