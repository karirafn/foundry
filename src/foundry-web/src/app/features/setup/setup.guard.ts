import { inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
    catchError(() => of(true)),
  );
}
