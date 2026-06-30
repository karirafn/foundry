import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import { WorkerRunDetail } from './worker-run.model';

@Injectable({ providedIn: 'root' })
export class WorkerRunService {
  private readonly _http = inject(HttpClient);

  getDetail(workerRunId: string): Observable<WorkerRunDetail | null> {
    return this._http.get<WorkerRunDetail>(`/api/workers/runs/${workerRunId}`).pipe(
      catchError((err: HttpErrorResponse) => {
        if (err.status === 404) {
          return of(null);
        }
        console.error(err);
        return of(null);
      }),
    );
  }

  getLog(workerRunId: string): Observable<string | null> {
    return this._http.get(`/api/workers/runs/${workerRunId}/log`, {
      responseType: 'text',
      observe: 'response',
    }).pipe(
      map((response) => {
        if (response.status === 204 || response.body === null) {
          return null;
        }
        return response.body;
      }),
      catchError((err: HttpErrorResponse) => {
        if (err.status === 204 || err.status === 404) {
          return of(null);
        }
        console.error(err);
        return of(null);
      }),
    );
  }
}
