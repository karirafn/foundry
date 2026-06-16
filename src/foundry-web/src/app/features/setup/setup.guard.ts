import { inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AccountSummary } from '../settings/accounts/account.model';

const ACCOUNTS_API = '/api/accounts';
const SETUP_PATH = '/setup';

export function setupGuard(): Observable<boolean | UrlTree> {
  const http = inject(HttpClient);
  const router = inject(Router);

  return http.get<AccountSummary[]>(ACCOUNTS_API).pipe(
    map((accounts) => {
      if (accounts.length === 0) {
        return router.parseUrl(SETUP_PATH);
      }
      return true;
    }),
    catchError((err: HttpErrorResponse) => {
      // Network error (status 0): API is unreachable — allow navigation so a fresh install
      // with no API running does not get stuck on the setup wizard indefinitely.
      // Server error (4xx/5xx): API is reachable but erroring for an unrelated reason —
      // setup is not the problem, so allow navigation and let the feature handle the error.
      return of(true);
    }),
  );
}
