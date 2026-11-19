import { inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, UrlTree } from '@angular/router';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { AccountSummary } from '../settings/accounts/account.model';
import { GlobalSettingsResponse } from '../../core/models/settings.model';

const ACCOUNTS_API = '/api/accounts';
const SETTINGS_API = '/api/settings';
const SETUP_PATH = '/setup';

/** Sends the operator to the setup wizard until an account and the Webhook Public Base URL exist (ADR 0116). */
export function setupGuard(): Observable<boolean | UrlTree> {
  const http = inject(HttpClient);
  const router = inject(Router);

  return forkJoin({
    accounts: http.get<AccountSummary[]>(ACCOUNTS_API),
    settings: http.get<GlobalSettingsResponse>(SETTINGS_API),
  }).pipe(
    map(({ accounts, settings }) => {
      if (accounts.length === 0 || !settings.webhookPublicBaseUrl) {
        return router.parseUrl(SETUP_PATH);
      }
      return true;
    }),
    catchError(() => {
      // Network error (status 0): API is unreachable — allow navigation so a fresh install
      // with no API running does not get stuck on the setup wizard indefinitely.
      // Server error (4xx/5xx): API is reachable but erroring for an unrelated reason —
      // setup is not the problem, so allow navigation and let the feature handle the error.
      return of(true);
    }),
  );
}
